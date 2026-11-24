using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Auth.IssueToken;
using OrderService.Application.Common;
using OrderService.Application.Orders.CancelOrder;
using OrderService.Application.Orders.ConfirmOrder;
using OrderService.Application.Orders.CreateOrder;
using OrderService.Application.Orders.GetOrderById;
using OrderService.Application.Orders.ListOrders;

namespace OrderService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();

        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<ConfirmOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
        services.AddScoped<GetOrderByIdHandler>();
        services.AddScoped<ListOrdersHandler>();
        services.AddScoped<IssueTokenHandler>();
        services.AddScoped<ConcurrencyRetry>();

        return services;
    }
}
