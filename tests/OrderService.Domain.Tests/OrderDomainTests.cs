using OrderService.Domain.Exceptions;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests;

public class CurrencyAndMoneyTests
{
    [Fact]
    public void Currency_normalizes_case_and_rejects_invalid_codes()
    {
        Assert.Equal("BRL", Currency.From("brl").Code);
        Assert.Equal(Currency.From("BRL"), Currency.From(" brl "));
        Assert.Throws<InvalidCurrencyException>(() => Currency.From("BR"));
        Assert.Throws<InvalidCurrencyException>(() => Currency.From("123"));
        Assert.Throws<InvalidCurrencyException>(() => Currency.From(" "));
    }

    [Fact]
    public void Money_rejects_negative_amounts_and_rounds_to_the_currency_scale()
    {
        var brl = Currency.From("BRL");
        Assert.Throws<InvalidMoneyException>(() => new Money(-0.01m, brl));

        var rounded = new Money(10.005m, brl);
        Assert.Equal(10.00m, rounded.Amount);

        var yen = new Money(10.5m, Currency.From("JPY"));
        Assert.Equal(10m, yen.Amount);
        Assert.Equal(2m, new Money(1.5m, Currency.From("JPY")).Amount);
    }

    [Fact]
    public void Money_addition_requires_the_same_currency()
    {
        var brl = new Money(10m, Currency.From("BRL"));
        var usd = new Money(10m, Currency.From("USD"));

        Assert.Equal(30m, brl.Add(new Money(20m, Currency.From("BRL"))).Amount);
        Assert.Throws<CurrencyMismatchException>(() => brl.Add(usd));
    }

    [Fact]
    public void Quantity_must_be_positive()
    {
        Assert.Equal(2, new Quantity(2).Value);
        Assert.Throws<InvalidQuantityException>(() => new Quantity(0));
        Assert.Throws<InvalidQuantityException>(() => new Quantity(-1));
    }
}

public class ProductTests
{
    [Fact]
    public void Reserve_decreases_available_stock_and_rejects_oversell()
    {
        var product = CreateMouse(available: 2);

        product.Reserve(2);

        Assert.Equal(0, product.AvailableQuantity);
        var error = Assert.Throws<InsufficientStockException>(() => product.Reserve(1));
        Assert.Equal(0, error.Available);
        Assert.Equal(0, product.AvailableQuantity);
    }

    [Fact]
    public void Release_returns_stock_and_rejects_non_positive_quantities()
    {
        var product = CreateMouse(available: 1);

        product.Release(4);

        Assert.Equal(5, product.AvailableQuantity);
        Assert.Throws<InvalidQuantityException>(() => product.Release(0));
    }

    [Fact]
    public void Release_does_not_overflow()
    {
        var product = CreateMouse(available: int.MaxValue);
        Assert.Throws<InvalidQuantityException>(() => product.Release(1));
        Assert.Equal(int.MaxValue, product.AvailableQuantity);
    }

    private static Product CreateMouse(int available) =>
        Product.Create(Guid.NewGuid(), "MOUSE-01", "Mouse sem fio", new Money(89.90m, Currency.From("BRL")), available);
}

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Place_calculates_total_as_the_sum_of_line_prices()
    {
        var order = Order.Place(
            Guid.NewGuid(),
            Currency.From("BRL"),
            [
                Line("Notebook", 4500m, 2, available: 5),
                Line("Mouse", 89.90m, 3, available: 10)
            ],
            Now);

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(9269.70m, order.Total.Amount);
        Assert.Equal("BRL", order.Currency.Code);
        Assert.Equal(9000m, order.Items.Single(item => item.ProductName == "Notebook").LineTotal.Amount);
        Assert.Equal(269.70m, order.Items.Single(item => item.ProductName == "Mouse").LineTotal.Amount);
    }

    [Fact]
    public void Place_rejects_empty_orders_duplicates_currency_mismatch_and_stock_overflow()
    {
        var customerId = Guid.NewGuid();
        var currency = Currency.From("BRL");

        Assert.Throws<EmptyOrderException>(() => Order.Place(customerId, currency, [], Now));
        Assert.Throws<DomainRuleException>(() => Order.Place(Guid.Empty, currency, [Line("Mouse", 10m, 1, 5)], Now));

        var productId = Guid.NewGuid();
        Assert.Throws<DuplicateOrderLineException>(() => Order.Place(
            customerId,
            currency,
            [Line("Mouse", 10m, 1, 5, productId), Line("Mouse", 10m, 1, 5, productId)],
            Now));

        Assert.Throws<CurrencyMismatchException>(() => Order.Place(
            customerId,
            Currency.From("USD"),
            [Line("Mouse", 10m, 1, 5)],
            Now));

        var stock = Assert.Throws<InsufficientStockException>(() => Order.Place(
            customerId,
            currency,
            [Line("Mouse", 10m, 6, available: 5)],
            Now));
        Assert.Equal(5, stock.Available);
        Assert.Equal(6, stock.Requested);
    }

    [Fact]
    public void Confirm_is_idempotent_and_only_allowed_from_placed()
    {
        var order = Place();

        Assert.True(order.Confirm(Now));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(Now, order.ConfirmedAt);
        Assert.False(order.Confirm(Now.AddMinutes(1)));
        Assert.Equal(Now, order.ConfirmedAt);

        var canceled = Place();
        canceled.Cancel(Now);
        Assert.Throws<InvalidOrderTransitionException>(() => canceled.Confirm(Now));
    }

    [Fact]
    public void Cancel_is_allowed_from_placed_and_confirmed_and_is_idempotent()
    {
        var placed = Place();
        var placedResult = placed.Cancel(Now);
        Assert.Equal(CancelResult.CanceledPlaced, placedResult);
        Assert.Equal(OrderStatus.Canceled, placed.Status);
        Assert.Equal(CancelResult.AlreadyCanceled, placed.Cancel(Now.AddMinutes(1)));

        var confirmed = Place();
        confirmed.Confirm(Now);
        var confirmedResult = confirmed.Cancel(Now.AddMinutes(5));
        Assert.Equal(CancelResult.CanceledConfirmed, confirmedResult);
        Assert.True(confirmedResult.ReleaseStock);
        Assert.False(placedResult.ReleaseStock);
    }

    private static Order Place() =>
        Order.Place(Guid.NewGuid(), Currency.From("BRL"), [Line("Mouse", 89.90m, 1, 10)], Now);

    private static OrderLine Line(string name, decimal price, int quantity, int available, Guid? productId = null) =>
        new(
            productId ?? Guid.NewGuid(),
            name,
            new Money(price, Currency.From("BRL")),
            new Quantity(quantity),
            available);
}
