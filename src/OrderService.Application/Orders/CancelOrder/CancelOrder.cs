using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;

namespace OrderService.Application.Orders.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId);

public sealed class CancelOrderHandler(
    IOrderRepository orders,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    IClock clock,
    ConcurrencyRetry retry,
    ILogger<CancelOrderHandler> logger)
{
    public Task<OrderDetailsDto> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        return retry.ExecuteAsync(() => CancelAsync(command, cancellationToken), cancellationToken);
    }

    private async Task<OrderDetailsDto> CancelAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(command.OrderId, cancellationToken)
            ?? throw new NotFoundException("order", command.OrderId);

        var previousStatus = order.Status;
        var result = order.Cancel(clock.UtcNow);
        if (!result.Changed)
        {
            logger.LogInformation("Cancelamento idempotente do pedido {OrderId}: já estava cancelado.", order.Id);
            return OrderMapper.ToDetails(order);
        }

        if (result.ReleaseStock)
        {
            var productIds = order.Items.Select(item => item.ProductId).Distinct().ToArray();
            var catalog = await products.GetByIdsAsync(productIds, cancellationToken);
            foreach (var item in order.Items)
            {
                if (!catalog.TryGetValue(item.ProductId, out var product))
                {
                    throw new NotFoundException("product", item.ProductId);
                }

                product.Release(item.Quantity.Value);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pedido {OrderId} cancelado a partir de {PreviousStatus}. Estoque liberado: {ReleasedStock}.",
            order.Id,
            previousStatus,
            result.ReleaseStock);

        return OrderMapper.ToDetails(order);
    }
}
