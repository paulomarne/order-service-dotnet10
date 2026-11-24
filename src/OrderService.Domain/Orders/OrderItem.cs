using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Orders;

public sealed class OrderItem
{
    private OrderItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; } = null!;

    public decimal UnitPriceAmount { get; private set; }

    public Currency Currency { get; private set; }

    public Quantity Quantity { get; private set; }

    public Money UnitPrice => new(UnitPriceAmount, Currency);

    public Money LineTotal => UnitPrice.Multiply(Quantity.Value);

    internal static OrderItem Create(Guid orderId, OrderLine line)
    {
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = line.ProductId,
            ProductName = line.ProductName.Trim(),
            UnitPriceAmount = line.UnitPrice.Amount,
            Currency = line.UnitPrice.Currency,
            Quantity = line.Quantity
        };
    }
}
