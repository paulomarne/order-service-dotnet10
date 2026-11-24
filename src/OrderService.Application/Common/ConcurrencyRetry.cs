using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Application.Abstractions;

namespace OrderService.Application.Common;

public sealed class ConcurrencyRetry(
    IPersistenceContext persistence,
    IOptions<ConcurrencyOptions> options,
    ILogger<ConcurrencyRetry> logger)
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, options.Value.MaxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action();
            }
            catch (ConcurrencyException exception) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Conflito de concorrência na tentativa {Attempt} de {MaxAttempts}. A operação será repetida.",
                    attempt,
                    maxAttempts);
                persistence.Clear();
            }
        }
    }
}
