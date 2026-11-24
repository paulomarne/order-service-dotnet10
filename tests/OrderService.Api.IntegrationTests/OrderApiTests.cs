using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderService.Infrastructure.Seeding;

namespace OrderService.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrderApiTests(ApiFixture fixture)
{
    [DockerFact]
    public async Task Health_is_anonymous_and_ready_checks_postgres()
    {
        var client = fixture.CreateClient();

        var live = await client.GetAsync("/health");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [DockerFact]
    public async Task OpenApi_document_exposes_bearer_auth()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bearer", document, StringComparison.Ordinal);
        Assert.Contains("/orders", document, StringComparison.Ordinal);
        Assert.Contains("/auth/token", document, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task Token_rejects_bad_credentials_and_orders_require_a_scoped_jwt()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClient();

        var anonymous = await client.GetAsync("/orders");
        var invalid = await client.PostAsJsonAsync("/auth/token", new { clientId = "dev-client", clientSecret = "wrong" });
        var missing = await client.PostAsJsonAsync("/auth/token", new { clientId = "", clientSecret = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("invalid_credentials", await ErrorCodeAsync(invalid));
    }

    [DockerFact]
    public async Task Create_get_and_list_cover_validation_filters_and_pagination()
    {
        await fixture.ResetAsync();
        var client = await AuthorizedClientAsync();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();

        var empty = await client.PostAsJsonAsync("/orders", new { customerId = customerA, currency = "BRL", items = Array.Empty<object>() });
        var zero = await client.PostAsJsonAsync("/orders", new
        {
            customerId = customerA,
            currency = "BRL",
            items = new[] { new { productId = ProductCatalog.MouseId, quantity = 0 } }
        });
        var missing = await client.PostAsJsonAsync("/orders", new
        {
            customerId = customerA,
            currency = "BRL",
            items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } }
        });
        var oversell = await client.PostAsJsonAsync("/orders", new
        {
            customerId = customerA,
            currency = "BRL",
            items = new[] { new { productId = ProductCatalog.NotebookId, quantity = 11 } }
        });
        var currency = await client.PostAsJsonAsync("/orders", new
        {
            customerId = customerA,
            currency = "USD",
            items = new[] { new { productId = ProductCatalog.MouseId, quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, oversell.StatusCode);
        Assert.Equal("insufficient_stock", await ErrorCodeAsync(oversell));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, currency.StatusCode);
        Assert.Equal(ProductCatalog.MouseStock(), await fixture.GetStockAsync(ProductCatalog.MouseId));

        var created = await client.PostAsJsonAsync("/orders", Payload(customerA, ProductCatalog.MouseId, 2, ProductCatalog.KeyboardId, 1));
        var order = await created.Content.ReadFromJsonAsync<OrderBody>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(order);
        Assert.EndsWith($"/orders/{order.Id}", created.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Equal("Placed", order.Status);
        Assert.Equal("BRL", order.Currency);
        Assert.Equal(429.70m, order.Total);
        Assert.Equal(2, order.Items.Count);
        Assert.True(created.Headers.Contains("X-Correlation-Id"));

        var fetched = await client.GetFromJsonAsync<OrderBody>($"/orders/{order.Id}");
        Assert.Equal(order.Id, fetched!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/orders/{Guid.NewGuid()}")).StatusCode);

        await client.PostAsJsonAsync("/orders", Payload(customerB, ProductCatalog.MonitorId, 1));
        var confirmedResponse = await client.PostAsync($"/orders/{order.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode);

        var byCustomer = await client.GetFromJsonAsync<PageBody>($"/orders?customerId={customerA}&page=1&pageSize=10");
        var byStatus = await client.GetFromJsonAsync<PageBody>("/orders?status=Confirmed");
        var page = await client.GetFromJsonAsync<PageBody>("/orders?page=1&pageSize=1");
        var future = await client.GetFromJsonAsync<PageBody>($"/orders?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}");
        var invalidPage = await client.GetAsync("/orders?page=0&pageSize=1000&status=unknown");

        Assert.Equal(1, byCustomer!.TotalCount);
        Assert.Equal(customerA, byCustomer.Items.Single().CustomerId);
        Assert.Equal(order.Id, byStatus!.Items.Single().Id);
        Assert.Equal(2, page!.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(0, future!.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    [DockerFact]
    public async Task Confirm_and_cancel_are_idempotent_and_move_stock_only_when_required()
    {
        await fixture.ResetAsync();
        var client = await AuthorizedClientAsync();
        var customerId = Guid.NewGuid();
        var initial = ProductCatalog.MouseStock();

        var placed = await CreateAsync(client, customerId, ProductCatalog.MouseId, 4);
        Assert.Equal(initial, await fixture.GetStockAsync(ProductCatalog.MouseId));

        var canceledPlaced = await client.PostAsync($"/orders/{placed.Id}/cancel", null);
        var canceledPlacedAgain = await client.PostAsync($"/orders/{placed.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, canceledPlaced.StatusCode);
        Assert.Equal("Canceled", (await canceledPlacedAgain.Content.ReadFromJsonAsync<OrderBody>())!.Status);
        Assert.Equal(initial, await fixture.GetStockAsync(ProductCatalog.MouseId));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{placed.Id}/confirm", null)).StatusCode);

        var order = await CreateAsync(client, customerId, ProductCatalog.MouseId, 4);
        var confirmed = await client.PostAsync($"/orders/{order.Id}/confirm", null);
        var confirmedAgain = await client.PostAsync($"/orders/{order.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Confirmed", (await confirmedAgain.Content.ReadFromJsonAsync<OrderBody>())!.Status);
        Assert.Equal(initial - 4, await fixture.GetStockAsync(ProductCatalog.MouseId));

        var canceled = await client.PostAsync($"/orders/{order.Id}/cancel", null);
        var canceledAgain = await client.PostAsync($"/orders/{order.Id}/cancel", null);
        Assert.Equal("Canceled", (await canceled.Content.ReadFromJsonAsync<OrderBody>())!.Status);
        Assert.Equal("Canceled", (await canceledAgain.Content.ReadFromJsonAsync<OrderBody>())!.Status);
        Assert.Equal(initial, await fixture.GetStockAsync(ProductCatalog.MouseId));
    }

    [DockerFact]
    public async Task Idempotency_key_replays_the_same_create_and_rejects_a_different_payload()
    {
        await fixture.ResetAsync();
        var client = await AuthorizedClientAsync();
        var customerId = Guid.NewGuid();
        var payload = Payload(customerId, ProductCatalog.NotebookId, 1);

        using var first = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(payload) };
        first.Headers.Add("Idempotency-Key", "create-notebook-1");
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(payload) };
        replay.Headers.Add("Idempotency-Key", "create-notebook-1");
        using var conflict = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(Payload(customerId, ProductCatalog.NotebookId, 2))
        };
        conflict.Headers.Add("Idempotency-Key", "create-notebook-1");

        var firstResponse = await client.SendAsync(first);
        var replayResponse = await client.SendAsync(replay);
        var conflictResponse = await client.SendAsync(conflict);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<OrderBody>();
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<OrderBody>();
        var listed = await client.GetFromJsonAsync<PageBody>($"/orders?customerId={customerId}");

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(firstBody!.Id, replayBody!.Id);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal("idempotency_conflict", await ErrorCodeAsync(conflictResponse));
        Assert.Equal(1, listed!.TotalCount);
        Assert.Equal(ProductCatalog.NotebookStock(), await fixture.GetStockAsync(ProductCatalog.NotebookId));
    }

    [DockerFact]
    public async Task Concurrent_confirms_cannot_oversell_the_same_stock()
    {
        await fixture.ResetAsync();
        var client = await AuthorizedClientAsync();
        var quantity = ProductCatalog.MouseStock();
        var first = await CreateAsync(client, Guid.NewGuid(), ProductCatalog.MouseId, quantity);
        var second = await CreateAsync(client, Guid.NewGuid(), ProductCatalog.MouseId, quantity);

        var results = await Task.WhenAll(
            client.PostAsync($"/orders/{first.Id}/confirm", null),
            client.PostAsync($"/orders/{second.Id}/confirm", null));

        var codes = results.Select(result => result.StatusCode).OrderBy(code => code).ToArray();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], codes);
        Assert.Equal(0, await fixture.GetStockAsync(ProductCatalog.MouseId));

        var confirmed = await client.GetFromJsonAsync<OrderBody>($"/orders/{(results[0].IsSuccessStatusCode ? first.Id : second.Id)}");
        var loserId = results[0].IsSuccessStatusCode ? second.Id : first.Id;
        var loser = await client.GetFromJsonAsync<OrderBody>($"/orders/{loserId}");
        Assert.Equal("Confirmed", confirmed!.Status);
        Assert.Equal("Placed", loser!.Status);
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/token", new { clientId = "dev-client", clientSecret = "dev-secret-change-me" });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static async Task<OrderBody> CreateAsync(HttpClient client, Guid customerId, Guid productId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/orders", Payload(customerId, productId, quantity));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderBody>())!;
    }

    private static object Payload(Guid customerId, Guid productId, int quantity, Guid? secondProductId = null, int secondQuantity = 0)
    {
        var items = new List<object> { new { productId, quantity } };
        if (secondProductId is Guid extra)
        {
            items.Add(new { productId = extra, quantity = secondQuantity });
        }

        return new { customerId, currency = "BRL", items };
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("errorCode").GetString();
    }

    private sealed record TokenBody(string AccessToken, string TokenType, int ExpiresIn);

    private sealed record OrderBody(
        Guid Id,
        Guid CustomerId,
        string Status,
        string Currency,
        decimal Total,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ConfirmedAt,
        DateTimeOffset? CanceledAt,
        List<ItemBody> Items);

    private sealed record ItemBody(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

    private sealed record PageBody(List<OrderBody> Items, int Page, int PageSize, int TotalCount, int TotalPages);
}
