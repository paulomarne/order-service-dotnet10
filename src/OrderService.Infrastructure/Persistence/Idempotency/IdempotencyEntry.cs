namespace OrderService.Infrastructure.Persistence.Idempotency;

public sealed class IdempotencyEntry
{
    public const string StateInProgress = "InProgress";

    public const string StateCompleted = "Completed";

    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public string State { get; set; } = StateInProgress;

    public int? StatusCode { get; set; }

    public string? ContentType { get; set; }

    public string? ResponseBody { get; set; }

    public string? Location { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
