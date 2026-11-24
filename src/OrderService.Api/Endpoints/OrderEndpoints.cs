using OrderService.Api.Auth;
using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Application.Orders.CancelOrder;
using OrderService.Application.Orders.ConfirmOrder;
using OrderService.Application.Orders.CreateOrder;
using OrderService.Application.Orders.GetOrderById;
using OrderService.Application.Orders.ListOrders;

namespace OrderService.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders")
            .WithTags("Orders")
            .RequireAuthorization(Policies.Orders);

        orders.MapPost("/", async (
                CreateOrderCommand command,
                CreateOrderHandler handler,
                CancellationToken cancellationToken) =>
            {
                var created = await handler.HandleAsync(command, cancellationToken);
                return Results.Created($"/orders/{created.Id}", created);
            })
            .WithName("CreateOrder")
            .WithSummary("Cria um pedido no estado Placed")
            .Produces<OrderDetailsDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/confirm", async (
                Guid id,
                ConfirmOrderHandler handler,
                CancellationToken cancellationToken) =>
            {
                var order = await handler.HandleAsync(new ConfirmOrderCommand(id), cancellationToken);
                return Results.Ok(order);
            })
            .WithName("ConfirmOrder")
            .WithSummary("Confirma um pedido e reserva o estoque. Idempotente.")
            .Produces<OrderDetailsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelOrderHandler handler,
                CancellationToken cancellationToken) =>
            {
                var order = await handler.HandleAsync(new CancelOrderCommand(id), cancellationToken);
                return Results.Ok(order);
            })
            .WithName("CancelOrder")
            .WithSummary("Cancela um pedido Placed ou Confirmed. Idempotente.")
            .Produces<OrderDetailsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapGet("/{id:guid}", async (
                Guid id,
                GetOrderByIdHandler handler,
                CancellationToken cancellationToken) =>
            {
                var order = await handler.HandleAsync(new GetOrderByIdQuery(id), cancellationToken);
                return Results.Ok(order);
            })
            .WithName("GetOrderById")
            .WithSummary("Consulta um pedido e seus itens")
            .Produces<OrderDetailsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapGet("/", async (
                [AsParameters] ListOrdersQuery query,
                ListOrdersHandler handler,
                CancellationToken cancellationToken) =>
            {
                var page = await handler.HandleAsync(query, cancellationToken);
                return Results.Ok(page);
            })
            .WithName("ListOrders")
            .WithSummary("Lista pedidos com paginação e filtros")
            .Produces<PagedResult<OrderDetailsDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
