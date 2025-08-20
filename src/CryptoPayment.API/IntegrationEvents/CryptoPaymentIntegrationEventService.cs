using System.Data.Common;

namespace eShop.CryptoPayment.API.IntegrationEvents;

public class CryptoPaymentIntegrationEventService : ICryptoPaymentIntegrationEventService
{
    private readonly IEventBus _eventBus;
    private readonly CryptoPaymentContext _cryptoPaymentContext;
    private readonly IIntegrationEventLogService _eventLogService;
    private readonly ILogger<CryptoPaymentIntegrationEventService> _logger;

    public CryptoPaymentIntegrationEventService(
        IEventBus eventBus,
        CryptoPaymentContext cryptoPaymentContext,
        IIntegrationEventLogService eventLogService,
        ILogger<CryptoPaymentIntegrationEventService> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _cryptoPaymentContext = cryptoPaymentContext ?? throw new ArgumentNullException(nameof(cryptoPaymentContext));
        _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishEventsThroughEventBusAsync(IntegrationEvent evt)
    {
        try
        {
            _logger.LogInformation("Publishing integration event: {IntegrationEventId_published} - ({@IntegrationEvent})", evt.Id, evt);

            await _eventLogService.MarkEventAsInProgressAsync(evt.Id);
            await _eventBus.PublishAsync(evt);
            await _eventLogService.MarkEventAsPublishedAsync(evt.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR Publishing integration event: {IntegrationEventId} - ({@IntegrationEvent})", evt.Id, evt);
            await _eventLogService.MarkEventAsFailedAsync(evt.Id);
        }
    }

    public async Task AddAndSaveEventAsync(IntegrationEvent evt)
    {
        _logger.LogInformation("Enqueuing integration event {IntegrationEventId} to repository ({@IntegrationEvent})", evt.Id, evt);

        await _eventLogService.SaveEventAsync(evt, _cryptoPaymentContext.GetCurrentTransaction());
    }
}

public static class CryptoPaymentContextExtension
{
    public static DbTransaction? GetCurrentTransaction(this CryptoPaymentContext context)
    {
        return context.Database.CurrentTransaction?.GetDbTransaction();
    }
}