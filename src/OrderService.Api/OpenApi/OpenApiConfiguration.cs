using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using OrderService.Api.Middleware;

namespace OrderService.Api.OpenApi;

public static class OpenApiConfiguration
{
    public static IServiceCollection AddOrderServiceOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Order Service";
                document.Info.Version = "v1";
                document.Info.Description =
                    "API de pedidos com autenticação JWT, reserva de estoque na confirmação e cancelamento idempotente.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Token obtido em POST /auth/token. Informe apenas o JWT; o prefixo Bearer é acrescentado pela UI."
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var path = context.Description.RelativePath ?? string.Empty;
                var method = context.Description.HttpMethod ?? string.Empty;
                var isPublic = path.StartsWith("auth", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("health", StringComparison.OrdinalIgnoreCase);

                if (!isPublic)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document, externalResource: null)] = []
                    });
                }

                if (HttpMethods.IsPost(method) && path.StartsWith("orders", StringComparison.OrdinalIgnoreCase))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = IdempotencyMiddleware.HeaderName,
                        In = ParameterLocation.Header,
                        Required = false,
                        Description = "Chave opcional para repetir a mesma requisição sem duplicar o efeito.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    });
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
