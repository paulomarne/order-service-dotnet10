using OrderService.Application.Auth.IssueToken;

namespace OrderService.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/token", async (
                IssueTokenCommand command,
                IssueTokenHandler handler,
                CancellationToken cancellationToken) =>
            {
                var token = await handler.HandleAsync(command, cancellationToken);
                return Results.Ok(token);
            })
            .WithName("IssueToken")
            .WithTags("Auth")
            .WithSummary("Emite um JWT de acesso")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        return app;
    }
}
