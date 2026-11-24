using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Seeding;
using Testcontainers.PostgreSql;

namespace OrderService.Api.IntegrationTests;

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = "Docker não está disponível neste ambiente; teste de integração ignorado.";
        }
    }
}

public static class DockerAvailability
{
    public static bool IsAvailable { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            if (!process.Start())
            {
                return false;
            }

            if (!process.WaitForExit(8000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("orders")
            .WithUsername("orders")
            .WithPassword("orders")
            .Build();

        await _container.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = _container?.GetConnectionString()
            ?? throw new InvalidOperationException("O container PostgreSQL de teste não foi iniciado.");

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Orders"] = connectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(descriptor =>
                    descriptor.ServiceType == typeof(OrdersDbContext)
                    || descriptor.ServiceType == typeof(DbContextOptions<OrdersDbContext>)
                    || descriptor.ServiceType == typeof(DbContextOptions)
                    || (descriptor.ServiceType.IsGenericType
                        && descriptor.ServiceType.GenericTypeArguments.Contains(typeof(OrdersDbContext))))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<OrdersDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
                options.UseSnakeCaseNamingConvention();
            });
        });
    }

    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await db.Orders.ExecuteDeleteAsync();
        await db.IdempotencyEntries.ExecuteDeleteAsync();

        foreach (var item in ProductCatalog.Items)
        {
            await db.Products
                .Where(product => product.Id == item.Id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(product => product.AvailableQuantity, item.AvailableQuantity));
        }
    }

    public async Task<int> GetStockAsync(Guid productId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await db.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.AvailableQuantity)
            .SingleAsync();
    }
}
