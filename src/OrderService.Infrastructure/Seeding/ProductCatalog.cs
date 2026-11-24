using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Products;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Seeding;

public static class ProductCatalog
{
    public static readonly Guid NotebookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid MouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid KeyboardId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid MonitorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public const string CurrencyCode = "BRL";

    public static int StockOf(Guid id) => Items.Single(item => item.Id == id).AvailableQuantity;

    public static int MouseStock() => StockOf(MouseId);

    public static int NotebookStock() => StockOf(NotebookId);

    public static IReadOnlyList<ProductSeed> Items { get; } =
    [
        new(NotebookId, "NB-14", "Notebook 14 polegadas", 4500.00m, 10),
        new(MouseId, "MOUSE-01", "Mouse sem fio", 89.90m, 50),
        new(KeyboardId, "KB-MEC", "Teclado mecânico", 249.90m, 30),
        new(MonitorId, "MON-27", "Monitor 27 polegadas", 1299.00m, 15)
    ];

    public static async Task EnsureSeededAsync(OrdersDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var currency = Currency.From(CurrencyCode);
        foreach (var item in Items)
        {
            db.Products.Add(Product.Create(
                item.Id,
                item.Sku,
                item.Name,
                new Money(item.UnitPrice, currency),
                item.AvailableQuantity));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public sealed record ProductSeed(Guid Id, string Sku, string Name, decimal UnitPrice, int AvailableQuantity);
}
