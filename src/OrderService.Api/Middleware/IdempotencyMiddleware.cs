using System.Security.Cryptography;
using System.Text;
using OrderService.Application.Abstractions;

namespace OrderService.Api.Middleware;

public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldApply(context.Request))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var rawValues))
        {
            await next(context);
            return;
        }

        var key = rawValues.ToString().Trim();
        if (key.Length is 0 or > 128)
        {
            await ProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "invalid_idempotency_key",
                "Idempotency-Key inválida",
                "A Idempotency-Key deve ter entre 1 e 128 caracteres.");
            return;
        }

        context.Request.EnableBuffering();
        var hash = await ComputeHashAsync(context.Request, context.RequestAborted);
        var store = context.RequestServices.GetRequiredService<IIdempotencyStore>();
        var persistence = context.RequestServices.GetRequiredService<IPersistenceContext>();
        var acquisition = await store.TryBeginAsync(key, hash, context.RequestAborted);

        switch (acquisition.Outcome)
        {
            case IdempotencyAcquisition.Replay when acquisition.Snapshot is { } snapshot:
                await ReplayAsync(context, snapshot);
                return;
            case IdempotencyAcquisition.HashConflict:
                await ProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "idempotency_conflict",
                    "Idempotency-Key em conflito",
                    "Esta Idempotency-Key já foi usada com uma requisição diferente.");
                return;
            case IdempotencyAcquisition.InProgress:
                await ProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "idempotency_in_progress",
                    "Requisição em andamento",
                    "Já existe uma requisição em andamento com esta Idempotency-Key.");
                return;
            case IdempotencyAcquisition.Started:
                break;
            default:
                await ProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "idempotency_conflict",
                    "Idempotency-Key em conflito",
                    "Não foi possível reutilizar esta Idempotency-Key.");
                return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        var businessSucceeded = false;

        try
        {
            await next(context);
            buffer.Position = 0;
            var body = await new StreamReader(buffer).ReadToEndAsync(context.RequestAborted);
            businessSucceeded = context.Response.StatusCode is >= 200 and < 300;

            if (businessSucceeded)
            {
                var location = context.Response.Headers.Location.ToString();
                await store.CompleteAsync(
                    key,
                    context.Response.StatusCode,
                    string.IsNullOrWhiteSpace(context.Response.ContentType) ? "application/json" : context.Response.ContentType,
                    body,
                    string.IsNullOrWhiteSpace(location) ? null : location,
                    context.RequestAborted);
            }
            else
            {
                persistence.Clear();
                await store.AbandonAsync(key, context.RequestAborted);
            }

            buffer.Position = 0;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch
        {
            if (!businessSucceeded)
            {
                persistence.Clear();
                await store.AbandonAsync(key, CancellationToken.None);
            }

            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool ShouldApply(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && request.Path.StartsWithSegments("/orders");

    private static async Task<string> ComputeHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        var payload = $"{request.Method}\n{request.Path}\n{body}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    private static Task ReplayAsync(HttpContext context, IdempotencySnapshot snapshot)
    {
        context.Response.StatusCode = snapshot.StatusCode;
        context.Response.ContentType = snapshot.ContentType;
        if (!string.IsNullOrWhiteSpace(snapshot.Location))
        {
            context.Response.Headers.Location = snapshot.Location;
        }

        return context.Response.WriteAsync(snapshot.Body);
    }

    private static Task ProblemAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string title,
        string detail)
    {
        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            instance: context.Request.Path,
            type: $"https://order-service.local/errors/{errorCode}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["traceId"] = context.TraceIdentifier
            }).ExecuteAsync(context);
    }
}
