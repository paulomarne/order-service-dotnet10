using System.Globalization;
using OrderService.Domain.Exceptions;

namespace OrderService.Domain.ValueObjects;

public readonly record struct Quantity
{
    public Quantity(int value)
    {
        if (value <= 0)
        {
            throw new InvalidQuantityException("A quantidade deve ser maior que zero.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
