namespace OrderService.Application.Abstractions;

public enum IdempotencyAcquisition
{
    Started = 0,
    Replay = 1,
    HashConflict = 2,
    InProgress = 3
}

public sealed record IdempotencySnapshot(int StatusCode, string ContentType, string Body, string? Location);

public sealed record IdempotencyAcquisitionResult(IdempotencyAcquisition Outcome, IdempotencySnapshot? Snapshot)
{
    public static IdempotencyAcquisitionResult Started() => new(IdempotencyAcquisition.Started, null);

    public static IdempotencyAcquisitionResult Replay(IdempotencySnapshot snapshot) =>
        new(IdempotencyAcquisition.Replay, snapshot);

    public static IdempotencyAcquisitionResult HashConflict() => new(IdempotencyAcquisition.HashConflict, null);

    public static IdempotencyAcquisitionResult InProgress() => new(IdempotencyAcquisition.InProgress, null);
}

public interface IIdempotencyStore
{
    Task<IdempotencyAcquisitionResult> TryBeginAsync(
        string key,
        string requestHash,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string key,
        int statusCode,
        string contentType,
        string body,
        string? location,
        CancellationToken cancellationToken);

    Task AbandonAsync(string key, CancellationToken cancellationToken);
}
