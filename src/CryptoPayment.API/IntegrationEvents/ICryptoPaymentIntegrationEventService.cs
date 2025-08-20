namespace eShop.CryptoPayment.API.IntegrationEvents;

public interface ICryptoPaymentIntegrationEventService
{
    Task PublishEventsThroughEventBusAsync(IntegrationEvent evt);
    Task AddAndSaveEventAsync(IntegrationEvent evt);
}