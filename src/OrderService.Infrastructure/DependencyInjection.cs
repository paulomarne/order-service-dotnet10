using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Abstractions;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Idempotency;
using OrderService.Infrastructure.Persistence.Queries;
using OrderService.Infrastructure.Persistence.Repositories;
using OrderService.Infrastructure.Time;

namespace OrderService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Orders")
            ?? throw new InvalidOperationException(
                "A connection string 'Orders' não foi configurada. Defina ConnectionStrings__Orders.");

        services.AddDbContext<OrdersDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null);
            });
            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<OrderRepository>();
        services.AddScoped<IOrderRepository>(provider => provider.GetRequiredService<OrderRepository>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderReadQueries, OrderReadQueries>();
        services.AddScoped<UnitOfWork>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<UnitOfWork>());
        services.AddScoped<IPersistenceContext>(provider => provider.GetRequiredService<UnitOfWork>());
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
