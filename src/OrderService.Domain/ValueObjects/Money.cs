using System.Globalization;
using OrderService.Domain.Exceptions;

namespace OrderService.Domain.ValueObjects;

public readonly record struct Money
{
    public Money(decimal amount, Currency currency)
    {
        if (amount < 0)
        {
            throw new InvalidMoneyException("O valor monetário não pode ser negativo.");
        }

        Amount = decimal.Round(amount, currency.Scale, MidpointRounding.ToEven);
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new CurrencyMismatchException(Currency.Code, other.Currency.Code);
        }

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity)
    {
        if (quantity < 0)
        {
            throw new InvalidQuantityException("A quantidade deve ser maior que zero.");
        }

        return new Money(Amount * quantity, Currency);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency.Code}");
}
