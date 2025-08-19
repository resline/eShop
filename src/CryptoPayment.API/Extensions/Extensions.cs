using eShop.CryptoPayment.API.IntegrationEvents;
using eShop.CryptoPayment.API.Services;
using eShop.CryptoPayment.API.Hubs;
using CryptoPayment.BlockchainServices.Extensions;
using Serilog;
using Serilog.Events;
using Serilog.Enrichers.Span;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace eShop.CryptoPayment.API.Extensions;

internal static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        
        // Add the authentication services to DI
        builder.AddDefaultAuthentication();

        // Add DbContext
        services.AddDbContext<CryptoPaymentContext>(options =>
        {
            options.UseNpgsql(builder.Configuration.GetConnectionString("cryptopaymentdb"));
        });
        builder.EnrichNpgsqlDbContext<CryptoPaymentContext>();

        services.AddMigration<CryptoPaymentContext, CryptoPaymentContextSeed>();

        // Add the integration services that consume the DbContext
        services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<CryptoPaymentContext>>();
        services.AddTransient<ICryptoPaymentIntegrationEventService, CryptoPaymentIntegrationEventService>();

        builder.AddRabbitMqEventBus("eventbus")
               .AddEventBusSubscriptions();

        services.AddHttpContextAccessor();
        services.AddTransient<IIdentityService, IdentityService>();

        // Add Redis for caching
        builder.AddRedis("cache");

        // Add crypto payment services
        services.AddScoped<ICryptoPaymentService, CryptoPaymentService>();
        services.AddScoped<IAddressGenerationService, RealAddressGenerationService>();
        
        // Add exchange rate service
        services.Configure<ExchangeRateOptions>(builder.Configuration.GetSection("ExchangeRate"));
        services.AddHttpClient<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        
        // Add real-time notification services
        services.AddSignalR();
        services.AddScoped<IPaymentNotificationService, PaymentNotificationService>();
        
        // Add blockchain services
        services.AddBlockchainServices(builder.Configuration);
    }

    private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
    {
        // Add subscriptions for integration events that this service handles
        // For example, when an order is placed, we might want to listen for that event
        // eventBus.AddSubscription<OrderStartedIntegrationEvent, OrderStartedIntegrationEventHandler>();
    }
}