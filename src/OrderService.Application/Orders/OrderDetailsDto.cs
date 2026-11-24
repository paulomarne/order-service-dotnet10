namespace OrderService.Application.Orders;

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record OrderDetailsDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    string Currency,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CanceledAt,
    IReadOnlyList<OrderItemDto> Items);
