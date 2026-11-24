namespace OrderService.Domain.Orders;

public readonly record struct CancelResult(bool Changed, bool ReleaseStock)
{
    public static CancelResult AlreadyCanceled { get; } = new(false, false);

    public static CancelResult CanceledPlaced { get; } = new(true, false);

    public static CancelResult CanceledConfirmed { get; } = new(true, true);
}
