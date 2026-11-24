using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Domain.Exceptions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.ConfirmOrder;

public sealed record ConfirmOrderCommand(Guid OrderId);

public sealed class ConfirmOrderHandler(
    IOrderRepository orders,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    IClock clock,
    ConcurrencyRetry retry,
    ILogger<ConfirmOrderHandler> logger)
{
    public Task<OrderDetailsDto> HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken)
    {
        return retry.ExecuteAsync(() => ConfirmAsync(command, cancellationToken), cancellationToken);
    }

    private async Task<OrderDetailsDto> ConfirmAsync(ConfirmOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(command.OrderId, cancellationToken)
            ?? throw new NotFoundException("order", command.OrderId);

        if (order.Status == OrderStatus.Confirmed)
        {
            logger.LogInformation("Confirmação idempotente do pedido {OrderId}: já estava confirmado.", order.Id);
            return OrderMapper.ToDetails(order);
        }

        var productIds = order.Items.Select(item => item.ProductId).Distinct().ToArray();
        var catalog = await products.GetByIdsAsync(productIds, cancellationToken);

        foreach (var item in order.Items)
        {
            if (!catalog.TryGetValue(item.ProductId, out var product))
            {
                throw new NotFoundException("product", item.ProductId);
            }

            if (product.AvailableQuantity < item.Quantity.Value)
            {
                throw new InsufficientStockException(
                    product.Id,
                    product.Name,
                    product.AvailableQuantity,
                    item.Quantity.Value);
            }
        }

        if (!order.Confirm(clock.UtcNow))
        {
            return OrderMapper.ToDetails(order);
        }

        foreach (var item in order.Items)
        {
            catalog[item.ProductId].Reserve(item.Quantity.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pedido {OrderId} confirmado. Estoque reservado para {ItemCount} item(ns).",
            order.Id,
            order.Items.Count);

        return OrderMapper.ToDetails(order);
    }
}
