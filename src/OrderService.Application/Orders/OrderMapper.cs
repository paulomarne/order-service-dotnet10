using OrderService.Domain.Orders;

namespace OrderService.Application.Orders;

public static class OrderMapper
{
    public static OrderDetailsDto ToDetails(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.Status.ToString(),
            order.Currency.Code,
            order.Total.Amount,
            order.CreatedAt,
            order.ConfirmedAt,
            order.CanceledAt,
            order.Items
                .Select(item => new OrderItemDto(
                    item.ProductId,
                    item.ProductName,
                    item.UnitPrice.Amount,
                    item.Quantity.Value,
                    item.LineTotal.Amount))
                .ToArray());
}
