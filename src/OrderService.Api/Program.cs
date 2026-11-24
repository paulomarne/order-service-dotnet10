using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.IdentityModel.Tokens;
using OrderService.Api.Auth;
using OrderService.Api.Endpoints;
using OrderService.Api.Errors;
using OrderService.Api.Health;
using OrderService.Api.Middleware;
using OrderService.Api.OpenApi;
using OrderService.Application;
using OrderService.Application.Abstractions;
using OrderService.Application.Auth;
using OrderService.Application.Common;
using OrderService.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var signingKey = builder.Configuration["Jwt:SigningKey"] ?? string.Empty;
if (signingKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey deve ter pelo menos 32 caracteres.");
}

if (!builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Testing")
    && signingKey.Contains("dev-only", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey de desenvolvimento não pode ser usado fora de Development/Testing.");
}

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

builder.Services.Configure<ConcurrencyOptions>(builder.Configuration.GetSection(ConcurrencyOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IClientCredentialValidator, ClientCredentialValidator>();
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("A seção Jwt não foi configurada.");

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Results.Problem(
                    title: "Não autorizado",
                    detail: "Informe um Bearer token válido.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    instance: context.Request.Path,
                    type: "https://order-service.local/errors/unauthorized",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "unauthorized",
                        ["traceId"] = context.HttpContext.TraceIdentifier
                    }).ExecuteAsync(context.HttpContext);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.Orders, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "orders");
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOrderServiceOpenApi();
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    logging.CombineLogs = true;
});
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    if (!app.Environment.IsEnvironment("Testing"))
    {
        app.MapScalarApiReference(options =>
        {
            options.WithTitle("Order Service");
            options.AddPreferredSecuritySchemes(["Bearer"]);
        });
    }
}

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapAuthEndpoints();
app.MapOrderEndpoints();

if (!IsEfDesignTime())
{
    await OrderService.Infrastructure.DatabaseInitializer.InitializeAsync(app.Services);
}

app.Run();

static bool IsEfDesignTime() =>
    AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
        string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore.Design", StringComparison.Ordinal));

public partial class Program;
