using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;

namespace OrderService.Infrastructure.Persistence.Idempotency;

public sealed class IdempotencyStore(OrdersDbContext db, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyAcquisitionResult> TryBeginAsync(
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Key == key, cancellationToken);

        if (existing is not null)
        {
            return MapExisting(existing, requestHash);
        }

        var entry = new IdempotencyEntry
        {
            Id = Guid.NewGuid(),
            Key = key,
            RequestHash = requestHash,
            State = IdempotencyEntry.StateInProgress,
            CreatedAt = clock.UtcNow
        };

        db.IdempotencyEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return IdempotencyAcquisitionResult.Started();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.IdempotencyEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

            return existing is null
                ? throw new InvalidOperationException("Não foi possível registrar a Idempotency-Key.")
                : MapExisting(existing, requestHash);
        }
    }

    public async Task CompleteAsync(
        string key,
        int statusCode,
        string contentType,
        string body,
        string? location,
        CancellationToken cancellationToken)
    {
        var entry = await db.IdempotencyEntries.FirstAsync(candidate => candidate.Key == key, cancellationToken);
        entry.State = IdempotencyEntry.StateCompleted;
        entry.StatusCode = statusCode;
        entry.ContentType = contentType;
        entry.ResponseBody = body;
        entry.Location = location;
        entry.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AbandonAsync(string key, CancellationToken cancellationToken)
    {
        var entry = await db.IdempotencyEntries.FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);
        if (entry is null || entry.State == IdempotencyEntry.StateCompleted)
        {
            return;
        }

        db.IdempotencyEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IdempotencyAcquisitionResult MapExisting(IdempotencyEntry existing, string requestHash)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return IdempotencyAcquisitionResult.HashConflict();
        }

        if (existing.State == IdempotencyEntry.StateCompleted && existing.StatusCode is int statusCode)
        {
            return IdempotencyAcquisitionResult.Replay(new IdempotencySnapshot(
                statusCode,
                existing.ContentType ?? "application/json",
                existing.ResponseBody ?? string.Empty,
                existing.Location));
        }

        return IdempotencyAcquisitionResult.InProgress();
    }
}
