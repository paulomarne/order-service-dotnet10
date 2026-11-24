using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderService.Application.Abstractions;
using OrderService.Application.Auth.IssueToken;
using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Application.Orders.CancelOrder;
using OrderService.Application.Orders.ConfirmOrder;
using OrderService.Application.Orders.CreateOrder;
using OrderService.Application.Orders.GetOrderById;
using OrderService.Application.Orders.ListOrders;
using OrderService.Domain.Exceptions;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Tests;

public class ConcurrencyRetryTests
{
    [Fact]
    public async Task Retries_concurrency_conflicts_and_then_succeeds()
    {
        var persistence = new FakePersistenceContext();
        var retry = new ConcurrencyRetry(
            persistence,
            Options.Create(new ConcurrencyOptions { MaxAttempts = 3 }),
            NullLogger<ConcurrencyRetry>.Instance);
        var attempts = 0;

        var result = await retry.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new ConcurrencyException("conflito");
            }

            return Task.FromResult("ok");
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, persistence.Clears);
    }

    [Fact]
    public async Task Gives_up_after_the_last_attempt()
    {
        var retry = new ConcurrencyRetry(
            new FakePersistenceContext(),
            Options.Create(new ConcurrencyOptions { MaxAttempts = 2 }),
            NullLogger<ConcurrencyRetry>.Instance);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            retry.ExecuteAsync<int>(() => throw new ConcurrencyException("conflito"), CancellationToken.None));
    }
}

public class IssueTokenHandlerTests
{
    [Fact]
    public async Task Issues_a_bearer_token_for_valid_credentials()
    {
        var handler = new IssueTokenHandler(
            new IssueTokenValidator(),
            new StaticCredentials(valid: true),
            new StaticTokens(),
            new FixedClock());

        var response = await handler.HandleAsync(
            new IssueTokenCommand { ClientId = "dev-client", ClientSecret = "secret" },
            CancellationToken.None);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal("token-for-dev-client", response.AccessToken);
        Assert.Equal(3600, response.ExpiresIn);
    }

    [Fact]
    public async Task Rejects_invalid_credentials_and_incomplete_payloads()
    {
        var handler = new IssueTokenHandler(
            new IssueTokenValidator(),
            new StaticCredentials(valid: false),
            new StaticTokens(),
            new FixedClock());

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            handler.HandleAsync(new IssueTokenCommand(), CancellationToken.None));

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            handler.HandleAsync(
                new IssueTokenCommand { ClientId = "dev-client", ClientSecret = "wrong" },
                CancellationToken.None));
    }
}

public class OrderHandlerTests
{
    private readonly FixedClock _clock = new();
    private readonly Product _mouse = Product.Create(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "MOUSE-01",
        "Mouse sem fio",
        new Money(89.90m, Currency.From("BRL")),
        5);
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Create_places_an_order_without_reserving_stock()
    {
        var handler = CreateHandler();

        var created = await handler.HandleAsync(Command(_mouse.Id, 2), CancellationToken.None);

        Assert.Equal("Placed", created.Status);
        Assert.Equal(179.80m, created.Total);
        Assert.Equal(5, _mouse.AvailableQuantity);
        Assert.Equal(1, _unitOfWork.Saves);
        Assert.Single(_orders.Orders);
    }

    [Fact]
    public async Task Create_rejects_unknown_products_insufficient_stock_and_duplicates()
    {
        var handler = CreateHandler();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(Command(Guid.NewGuid(), 1), CancellationToken.None));

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            handler.HandleAsync(Command(_mouse.Id, 6), CancellationToken.None));

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            handler.HandleAsync(
                new CreateOrderCommand
                {
                    CustomerId = Guid.NewGuid(),
                    Currency = "BRL",
                    Items =
                    [
                        new CreateOrderItem { ProductId = _mouse.Id, Quantity = 1 },
                        new CreateOrderItem { ProductId = _mouse.Id, Quantity = 1 }
                    ]
                },
                CancellationToken.None));

        Assert.Empty(_orders.Orders);
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Equal(5, _mouse.AvailableQuantity);
    }

    [Fact]
    public async Task Confirm_reserves_stock_once_and_cancel_confirmed_releases_it_once()
    {
        var created = await CreateHandler().HandleAsync(Command(_mouse.Id, 2), CancellationToken.None);
        var confirm = ConfirmHandler();
        var cancel = CancelHandler();

        var confirmed = await confirm.HandleAsync(new ConfirmOrderCommand(created.Id), CancellationToken.None);
        var again = await confirm.HandleAsync(new ConfirmOrderCommand(created.Id), CancellationToken.None);

        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal("Confirmed", again.Status);
        Assert.Equal(3, _mouse.AvailableQuantity);

        var canceled = await cancel.HandleAsync(new CancelOrderCommand(created.Id), CancellationToken.None);
        var canceledAgain = await cancel.HandleAsync(new CancelOrderCommand(created.Id), CancellationToken.None);

        Assert.Equal("Canceled", canceled.Status);
        Assert.Equal("Canceled", canceledAgain.Status);
        Assert.Equal(5, _mouse.AvailableQuantity);
    }

    [Fact]
    public async Task Cancel_placed_does_not_change_stock_and_blocks_a_later_confirm()
    {
        var created = await CreateHandler().HandleAsync(Command(_mouse.Id, 2), CancellationToken.None);

        var canceled = await CancelHandler().HandleAsync(new CancelOrderCommand(created.Id), CancellationToken.None);

        Assert.Equal("Canceled", canceled.Status);
        Assert.Equal(5, _mouse.AvailableQuantity);
        await Assert.ThrowsAsync<InvalidOrderTransitionException>(() =>
            ConfirmHandler().HandleAsync(new ConfirmOrderCommand(created.Id), CancellationToken.None));
        Assert.Equal(5, _mouse.AvailableQuantity);
    }

    [Fact]
    public async Task Confirm_fails_when_stock_was_taken_and_leaves_the_order_placed()
    {
        var created = await CreateHandler().HandleAsync(Command(_mouse.Id, 5), CancellationToken.None);
        _mouse.Reserve(5);
        var savesBefore = _unitOfWork.Saves;

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            ConfirmHandler().HandleAsync(new ConfirmOrderCommand(created.Id), CancellationToken.None));

        Assert.Equal(OrderStatus.Placed, _orders.Orders.Single().Status);
        Assert.Equal(0, _mouse.AvailableQuantity);
        Assert.Equal(savesBefore, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Queries_normalize_pagination_and_reject_invalid_filters()
    {
        var queries = new FakeQueries();
        var handler = new ListOrdersHandler(new ListOrdersValidator(), queries);

        await handler.HandleAsync(new ListOrdersQuery { Status = "confirmed" }, CancellationToken.None);

        Assert.NotNull(queries.Last);
        Assert.Equal(1, queries.Last!.Page);
        Assert.Equal(20, queries.Last.PageSize);
        Assert.Equal(OrderStatus.Confirmed, queries.Last.Status);

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            handler.HandleAsync(new ListOrdersQuery { Page = 0, PageSize = 500, Status = "nope" }, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetOrderByIdHandler(queries).HandleAsync(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None));
    }

    private CreateOrderHandler CreateHandler() => new(
        new CreateOrderValidator(),
        new FakeProductRepository(_mouse),
        _orders,
        _unitOfWork,
        _clock,
        Retry(),
        NullLogger<CreateOrderHandler>.Instance);

    private ConfirmOrderHandler ConfirmHandler() => new(
        _orders,
        new FakeProductRepository(_mouse),
        _unitOfWork,
        _clock,
        Retry(),
        NullLogger<ConfirmOrderHandler>.Instance);

    private CancelOrderHandler CancelHandler() => new(
        _orders,
        new FakeProductRepository(_mouse),
        _unitOfWork,
        _clock,
        Retry(),
        NullLogger<CancelOrderHandler>.Instance);

    private static ConcurrencyRetry Retry() => new(
        new FakePersistenceContext(),
        Options.Create(new ConcurrencyOptions { MaxAttempts = 3 }),
        NullLogger<ConcurrencyRetry>.Instance);

    private static CreateOrderCommand Command(Guid productId, int quantity) => new()
    {
        CustomerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Currency = "BRL",
        Items = [new CreateOrderItem { ProductId = productId, Quantity = quantity }]
    };
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakePersistenceContext : IPersistenceContext
{
    public int Clears { get; private set; }

    public void Clear() => Clears++;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Orders { get; } = [];

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.FirstOrDefault(order => order.Id == id));
}

internal sealed class FakeProductRepository(params Product[] products) : IProductRepository
{
    public Task<IReadOnlyDictionary<Guid, Product>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var wanted = ids.ToHashSet();
        IReadOnlyDictionary<Guid, Product> result = products
            .Where(product => wanted.Contains(product.Id))
            .ToDictionary(product => product.Id);
        return Task.FromResult(result);
    }
}

internal sealed class StaticCredentials(bool valid) : IClientCredentialValidator
{
    public bool Validate(string clientId, string clientSecret) => valid;
}

internal sealed class StaticTokens : ITokenIssuer
{
    public AccessToken Issue(string subject, DateTimeOffset now) => new($"token-for-{subject}", 3600);
}

internal sealed class FakeQueries : IOrderReadQueries
{
    public ListOrdersCriteria? Last { get; private set; }

    public Task<OrderDetailsDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<OrderDetailsDto?>(null);

    public Task<PagedResult<OrderDetailsDto>> ListAsync(ListOrdersCriteria criteria, CancellationToken cancellationToken)
    {
        Last = criteria;
        return Task.FromResult(new PagedResult<OrderDetailsDto>([], criteria.Page, criteria.PageSize, 0));
    }
}
