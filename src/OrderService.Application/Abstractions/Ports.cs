using OrderService.Domain.Orders;
using OrderService.Domain.Products;

namespace OrderService.Application.Abstractions;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

public interface IProductRepository
{
    Task<IReadOnlyDictionary<Guid, Product>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPersistenceContext
{
    void Clear();
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IClientCredentialValidator
{
    bool Validate(string clientId, string clientSecret);
}

public readonly record struct AccessToken(string Value, int ExpiresInSeconds);

public interface ITokenIssuer
{
    AccessToken Issue(string subject, DateTimeOffset now);
}
