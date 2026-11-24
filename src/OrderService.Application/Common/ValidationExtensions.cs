using FluentValidation;
using OrderService.Application.Abstractions;

namespace OrderService.Application.Common;

public static class ValidationExtensions
{
    public static async Task ThrowIfInvalidAsync<T>(
        this IValidator<T> validator,
        T instance,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(error => string.IsNullOrWhiteSpace(error.PropertyName) ? string.Empty : error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

        throw new ApplicationValidationException(errors);
    }
}
