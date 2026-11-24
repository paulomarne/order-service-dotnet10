using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Products;

public sealed class Product
{
    private Product()
    {
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public decimal UnitPriceAmount { get; private set; }

    public Currency Currency { get; private set; }

    public Money UnitPrice => new(UnitPriceAmount, Currency);

    public int AvailableQuantity { get; private set; }

    public static Product Create(Guid id, string sku, string name, Money unitPrice, int availableQuantity)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("invalid_product", "O produto é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainRuleException("invalid_product", "O SKU é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("invalid_product", "O nome do produto é obrigatório.");
        }

        if (availableQuantity < 0)
        {
            throw new DomainRuleException("invalid_stock", "O estoque não pode ser negativo.");
        }

        return new Product
        {
            Id = id,
            Sku = sku.Trim(),
            Name = name.Trim(),
            UnitPriceAmount = unitPrice.Amount,
            Currency = unitPrice.Currency,
            AvailableQuantity = availableQuantity
        };
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidQuantityException("A quantidade deve ser maior que zero.");
        }

        if (AvailableQuantity < quantity)
        {
            throw new InsufficientStockException(Id, Name, AvailableQuantity, quantity);
        }

        AvailableQuantity -= quantity;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidQuantityException("A quantidade deve ser maior que zero.");
        }

        if (AvailableQuantity > int.MaxValue - quantity)
        {
            throw new InvalidQuantityException("A liberação de estoque excederia o limite suportado.");
        }

        AvailableQuantity += quantity;
    }
}
