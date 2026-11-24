using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Domain.Products;

namespace OrderService.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository(OrdersDbContext db) : IProductRepository
{
    public async Task<IReadOnlyDictionary<Guid, Product>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0)
        {
            return new Dictionary<Guid, Product>();
        }

        var products = await db.Products
            .Where(product => idList.Contains(product.Id))
            .ToListAsync(cancellationToken);

        return products.ToDictionary(product => product.Id);
    }
}
