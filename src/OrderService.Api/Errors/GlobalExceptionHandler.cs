using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using OrderService.Application.Abstractions;
using OrderService.Domain.Exceptions;

namespace OrderService.Api.Errors;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return false;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        switch (exception)
        {
            case ApplicationValidationException validation:
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "validation_error",
                    "Requisição inválida",
                    validation.Message,
                    traceId,
                    validation.Errors,
                    cancellationToken);
                return true;

            case InvalidCredentialsException:
                logger.LogWarning("Tentativa de autenticação rejeitada. TraceId: {TraceId}", traceId);
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status401Unauthorized,
                    "invalid_credentials",
                    "Não autorizado",
                    exception.Message,
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;

            case NotFoundException notFound:
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    notFound.Code,
                    "Recurso não encontrado",
                    notFound.Message,
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;

            case InsufficientStockException stock:
                logger.LogInformation(stock, "Estoque insuficiente. TraceId: {TraceId}", traceId);
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    stock.Code,
                    "Estoque insuficiente",
                    stock.Message,
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;

            case InvalidOrderTransitionException transition:
                logger.LogInformation(transition, "Transição de pedido rejeitada. TraceId: {TraceId}", traceId);
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    transition.Code,
                    "Transição inválida",
                    transition.Message,
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;

            case DomainException domain:
                logger.LogInformation(domain, "Regra de domínio rejeitada. TraceId: {TraceId}", traceId);
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    domain.Code,
                    "Regra de negócio violada",
                    domain.Message,
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;

            case ConcurrencyException concurrency:
                logger.LogWarning(concurrency, "Conflito de concorrência esgotou as tentativas. TraceId: {TraceId}", traceId);
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    "concurrency_conflict",
                    "Conflito de concorrência",
                    "Não foi possível concluir a operação porque os dados foram alterados por outra requisição. Tente novamente.",
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;

            default:
                logger.LogError(exception, "Erro não tratado. TraceId: {TraceId}", traceId);
                await WriteAsync(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "unexpected_error",
                    "Erro interno",
                    "Ocorreu um erro inesperado.",
                    traceId,
                    errors: null,
                    cancellationToken);
                return true;
        }
    }

    private static Task WriteAsync(
        HttpContext httpContext,
        int statusCode,
        string errorCode,
        string title,
        string detail,
        string traceId,
        IReadOnlyDictionary<string, string[]>? errors,
        CancellationToken cancellationToken)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["traceId"] = traceId
        };

        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            instance: httpContext.Request.Path,
            type: $"https://order-service.local/errors/{errorCode}",
            extensions: extensions).ExecuteAsync(httpContext);
    }
}
