namespace OrderService.Application.Common;

public sealed class ConcurrencyOptions
{
    public const string SectionName = "Concurrency";

    public int MaxAttempts { get; set; } = 3;
}
