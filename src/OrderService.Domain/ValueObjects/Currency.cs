using OrderService.Domain.Exceptions;

namespace OrderService.Domain.ValueObjects;

public readonly record struct Currency
{
    private Currency(string code) => Code = code;

    public string Code { get; }

    public int Scale => Code switch
    {
        "JPY" or "KRW" => 0,
        "BHD" or "KWD" or "OMR" => 3,
        _ => 2
    };

    public static Currency From(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidCurrencyException("A moeda é obrigatória.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new InvalidCurrencyException("A moeda deve ser um código ISO 4217 de 3 letras.");
        }

        return new Currency(normalized);
    }

    public override string ToString() => Code;
}
