using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders;

namespace OrderService.Infrastructure.Persistence.Queries;

public sealed class OrderReadQueries(OrdersDbContext db) : IOrderReadQueries
{
    public async Task<OrderDetailsDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Orders
            .AsNoTracking()
            .Where(order => order.Id == id)
            .Select(order => new OrderProjection(
                order.Id,
                order.CustomerId,
                order.Status,
                order.Currency,
                order.TotalAmount,
                order.CreatedAt,
                order.ConfirmedAt,
                order.CanceledAt,
                order.Items
                    .OrderBy(item => item.ProductName)
                    .Select(item => new ItemProjection(
                        item.ProductId,
                        item.ProductName,
                        item.UnitPriceAmount,
                        item.Quantity))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return row?.ToDto();
    }

    public async Task<PagedResult<OrderDetailsDto>> ListAsync(
        ListOrdersCriteria criteria,
        CancellationToken cancellationToken)
    {
        var filtered = db.Orders.AsNoTracking().AsQueryable();

        if (criteria.CustomerId is Guid customerId)
        {
            filtered = filtered.Where(order => order.CustomerId == customerId);
        }

        if (criteria.Status is not null)
        {
            filtered = filtered.Where(order => order.Status == criteria.Status);
        }

        if (criteria.From is DateTimeOffset from)
        {
            filtered = filtered.Where(order => order.CreatedAt >= from);
        }

        if (criteria.To is DateTimeOffset to)
        {
            filtered = filtered.Where(order => order.CreatedAt <= to);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var rows = await filtered
            .OrderByDescending(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .AsSplitQuery()
            .Select(order => new OrderProjection(
                order.Id,
                order.CustomerId,
                order.Status,
                order.Currency,
                order.TotalAmount,
                order.CreatedAt,
                order.ConfirmedAt,
                order.CanceledAt,
                order.Items
                    .OrderBy(item => item.ProductName)
                    .Select(item => new ItemProjection(
                        item.ProductId,
                        item.ProductName,
                        item.UnitPriceAmount,
                        item.Quantity))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderDetailsDto>(
            rows.Select(row => row.ToDto()).ToArray(),
            criteria.Page,
            criteria.PageSize,
            totalCount);
    }

    private sealed record ItemProjection(Guid ProductId, string ProductName, decimal UnitPriceAmount, Domain.ValueObjects.Quantity Quantity);

    private sealed record OrderProjection(
        Guid Id,
        Guid CustomerId,
        Domain.Orders.OrderStatus Status,
        Domain.ValueObjects.Currency Currency,
        decimal TotalAmount,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ConfirmedAt,
        DateTimeOffset? CanceledAt,
        List<ItemProjection> Items)
    {
        public OrderDetailsDto ToDto() =>
            new(
                Id,
                CustomerId,
                Status.ToString(),
                Currency.Code,
                TotalAmount,
                CreatedAt,
                ConfirmedAt,
                CanceledAt,
                Items.Select(item => new OrderItemDto(
                    item.ProductId,
                    item.ProductName,
                    item.UnitPriceAmount,
                    item.Quantity.Value,
                    decimal.Round(item.UnitPriceAmount * item.Quantity.Value, Currency.Scale, MidpointRounding.ToEven)))
                    .ToArray());
    }
}
