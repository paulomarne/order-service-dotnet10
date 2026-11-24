using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Orders;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public Currency Currency { get; private set; }

    public decimal TotalAmount { get; private set; }

    public Money Total => new(TotalAmount, Currency);

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public static Order Place(
        Guid customerId,
        Currency currency,
        IReadOnlyList<OrderLine> lines,
        DateTimeOffset createdAt)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainRuleException("invalid_customer", "O cliente é obrigatório.");
        }

        if (lines.Count == 0)
        {
            throw new EmptyOrderException();
        }

        var duplicate = lines.GroupBy(line => line.ProductId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new DuplicateOrderLineException(duplicate.Key);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Placed,
            Currency = currency,
            CreatedAt = createdAt
        };

        foreach (var line in lines)
        {
            if (line.ProductId == Guid.Empty)
            {
                throw new DomainRuleException("invalid_product", "O produto é obrigatório.");
            }

            if (string.IsNullOrWhiteSpace(line.ProductName))
            {
                throw new DomainRuleException("invalid_product", "O nome do produto é obrigatório.");
            }

            if (line.UnitPrice.Currency != currency)
            {
                throw new CurrencyMismatchException(currency.Code, line.UnitPrice.Currency.Code);
            }

            if (line.AvailableQuantity < line.Quantity.Value)
            {
                throw new InsufficientStockException(
                    line.ProductId,
                    line.ProductName,
                    line.AvailableQuantity,
                    line.Quantity.Value);
            }

            order._items.Add(OrderItem.Create(order.Id, line));
        }

        order.RecalculateTotal();
        return order;
    }

    /// <summary>
    /// Confirma o pedido. Retorna false quando ele já está confirmado (idempotente).
    /// A baixa de estoque fica a cargo do caso de uso, que só deve ocorrer quando o retorno é true.
    /// </summary>
    public bool Confirm(DateTimeOffset confirmedAt)
    {
        if (Status == OrderStatus.Confirmed)
        {
            return false;
        }

        if (Status != OrderStatus.Placed)
        {
            throw new InvalidOrderTransitionException(Status.ToString(), nameof(OrderStatus.Confirmed));
        }

        Status = OrderStatus.Confirmed;
        ConfirmedAt = confirmedAt;
        return true;
    }

    /// <summary>
    /// Cancela o pedido. Pedido já cancelado devolve um resultado sem efeito.
    /// Estoque só é liberado quando o pedido estava confirmado.
    /// </summary>
    public CancelResult Cancel(DateTimeOffset canceledAt)
    {
        if (Status == OrderStatus.Canceled)
        {
            return CancelResult.AlreadyCanceled;
        }

        if (Status is not (OrderStatus.Placed or OrderStatus.Confirmed))
        {
            throw new InvalidOrderTransitionException(Status.ToString(), nameof(OrderStatus.Canceled));
        }

        var result = Status == OrderStatus.Confirmed
            ? CancelResult.CanceledConfirmed
            : CancelResult.CanceledPlaced;

        Status = OrderStatus.Canceled;
        CanceledAt = canceledAt;
        return result;
    }

    private void RecalculateTotal()
    {
        var total = _items.Aggregate(Money.Zero(Currency), (sum, item) => sum.Add(item.LineTotal));
        TotalAmount = total.Amount;
    }
}
