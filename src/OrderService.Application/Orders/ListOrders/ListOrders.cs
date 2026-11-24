using FluentValidation;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.ListOrders;

public sealed record ListOrdersQuery
{
    public Guid? CustomerId { get; init; }

    public string? Status { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

public sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    private static readonly HashSet<string> Statuses =
        Enum.GetNames<OrderStatus>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public ListOrdersValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A página deve ser maior ou igual a 1.")
            .When(query => query.Page.HasValue);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("O tamanho da página deve estar entre 1 e 100.")
            .When(query => query.PageSize.HasValue);

        RuleFor(query => query.Status)
            .Must(status => status is null || Statuses.Contains(status))
            .WithMessage("O status informado não é válido. Use Draft, Placed, Confirmed ou Canceled.");

        RuleFor(query => query)
            .Must(query => query.From is null || query.To is null || query.From <= query.To)
            .WithMessage("A data inicial não pode ser posterior à data final.");
    }
}

public sealed class ListOrdersHandler(
    IValidator<ListOrdersQuery> validator,
    IOrderReadQueries queries)
{
    public async Task<PagedResult<OrderDetailsDto>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ThrowIfInvalidAsync(query, cancellationToken);

        OrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            status = Enum.Parse<OrderStatus>(query.Status, ignoreCase: true);
        }

        var criteria = new ListOrdersCriteria(
            query.CustomerId,
            status,
            query.From,
            query.To,
            query.Page ?? 1,
            query.PageSize ?? 20);

        return await queries.ListAsync(criteria, cancellationToken);
    }
}
