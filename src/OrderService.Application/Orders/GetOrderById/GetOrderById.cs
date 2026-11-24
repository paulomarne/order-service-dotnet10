using OrderService.Application.Abstractions;

namespace OrderService.Application.Orders.GetOrderById;

public sealed record GetOrderByIdQuery(Guid OrderId);

public sealed class GetOrderByIdHandler(IOrderReadQueries queries)
{
    public async Task<OrderDetailsDto> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        return await queries.GetByIdAsync(query.OrderId, cancellationToken)
            ?? throw new NotFoundException("order", query.OrderId);
    }
}
