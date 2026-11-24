namespace OrderService.Domain.Exceptions;

public sealed class EmptyOrderException()
    : DomainException("empty_order", "O pedido deve conter ao menos um item.");

public sealed class InvalidQuantityException(string message)
    : DomainException("invalid_quantity", message);

public sealed class InvalidMoneyException(string message)
    : DomainException("invalid_money", message);

public sealed class InvalidCurrencyException(string message)
    : DomainException("invalid_currency", message);

public sealed class CurrencyMismatchException(string orderCurrency, string productCurrency)
    : DomainException(
        "currency_mismatch",
        $"A moeda do produto ({productCurrency}) não corresponde à moeda do pedido ({orderCurrency}).");

public sealed class DuplicateOrderLineException(Guid productId)
    : DomainException(
        "duplicate_order_line",
        $"O produto '{productId}' está repetido no pedido. Envie uma única linha por produto.");

public sealed class InvalidOrderTransitionException(string from, string to)
    : DomainException("invalid_transition", $"Não é possível alterar o pedido de {from} para {to}.");

public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(Guid productId, string productName, int available, int requested)
        : base(
            "insufficient_stock",
            $"Estoque insuficiente para o produto '{productName}'. Disponível: {available}. Solicitado: {requested}.")
    {
        ProductId = productId;
        Available = available;
        Requested = requested;
    }

    public Guid ProductId { get; }

    public int Available { get; }

    public int Requested { get; }
}
