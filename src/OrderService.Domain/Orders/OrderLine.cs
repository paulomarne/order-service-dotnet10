using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Orders;

public sealed record OrderLine(
    Guid ProductId,
    string ProductName,
    Money UnitPrice,
    Quantity Quantity,
    int AvailableQuantity);
