using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Domain.Orders;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Orders.CreateOrder;

public sealed record CreateOrderItem
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}

public sealed record CreateOrderCommand
{
    public Guid CustomerId { get; init; }

    public string Currency { get; init; } = string.Empty;

    public List<CreateOrderItem> Items { get; init; } = [];
}

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(command => command.CustomerId)
            .NotEmpty()
            .WithMessage("O cliente é obrigatório.");

        RuleFor(command => command.Currency)
            .NotEmpty()
            .WithMessage("A moeda é obrigatória.")
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("A moeda deve ser um código ISO 4217 de 3 letras.");

        RuleFor(command => command.Items)
            .NotNull()
            .WithMessage("Os itens são obrigatórios.")
            .NotEmpty()
            .WithMessage("O pedido deve conter ao menos um item.");

        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(line => line.ProductId)
                .NotEmpty()
                .WithMessage("O produto é obrigatório.");

            item.RuleFor(line => line.Quantity)
                .GreaterThan(0)
                .WithMessage("A quantidade deve ser maior que zero.");
        });

        RuleFor(command => command.Items)
            .Must(items => items.Select(item => item.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Cada produto deve aparecer em uma única linha.")
            .When(command => command.Items is { Count: > 0 });
    }
}

public sealed class CreateOrderHandler(
    IValidator<CreateOrderCommand> validator,
    IProductRepository products,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IClock clock,
    ConcurrencyRetry retry,
    ILogger<CreateOrderHandler> logger)
{
    public Task<OrderDetailsDto> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        return retry.ExecuteAsync(() => CreateAsync(command, cancellationToken), cancellationToken);
    }

    private async Task<OrderDetailsDto> CreateAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        await validator.ThrowIfInvalidAsync(command, cancellationToken);

        var currency = Currency.From(command.Currency);
        var productIds = command.Items.Select(item => item.ProductId).ToArray();
        var catalog = await products.GetByIdsAsync(productIds, cancellationToken);

        var lines = new List<OrderLine>(command.Items.Count);
        foreach (var item in command.Items)
        {
            if (!catalog.TryGetValue(item.ProductId, out var product))
            {
                throw new NotFoundException("product", item.ProductId);
            }

            lines.Add(new OrderLine(
                product.Id,
                product.Name,
                product.UnitPrice,
                new Quantity(item.Quantity),
                product.AvailableQuantity));
        }

        var order = Order.Place(command.CustomerId, currency, lines, clock.UtcNow);
        await orders.AddAsync(order, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pedido {OrderId} criado com status {Status} para o cliente {CustomerId}. Total {Total} {Currency}.",
            order.Id,
            order.Status,
            order.CustomerId,
            order.Total.Amount,
            order.Currency.Code);

        return OrderMapper.ToDetails(order);
    }
}
