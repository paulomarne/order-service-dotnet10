using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Domain.Orders;

namespace OrderService.Application.Abstractions;

public sealed record ListOrdersCriteria(
    Guid? CustomerId,
    OrderStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public interface IOrderReadQueries
{
    Task<OrderDetailsDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<OrderDetailsDto>> ListAsync(ListOrdersCriteria criteria, CancellationToken cancellationToken);
}
