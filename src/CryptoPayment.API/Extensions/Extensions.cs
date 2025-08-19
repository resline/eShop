using eShop.CryptoPayment.API.IntegrationEvents;
using CryptoPayment.BlockchainServices.Extensions;

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

        // Add crypto payment services
        services.AddScoped<ICryptoPaymentService, CryptoPaymentService>();
        services.AddScoped<IAddressGenerationService, AddressGenerationService>();
        
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