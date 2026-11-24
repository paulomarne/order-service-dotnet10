using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;

namespace OrderService.Infrastructure.Persistence;

public sealed class UnitOfWork(OrdersDbContext db) : IUnitOfWork, IPersistenceContext
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException(
                "O registro foi alterado por outra transação.",
                exception);
        }
    }

    public void Clear() => db.ChangeTracker.Clear();
}
