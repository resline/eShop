namespace eShop.CryptoPayment.API.IntegrationEvents.Events;

public record CryptoPaymentStatusChangedIntegrationEvent : IntegrationEvent
{
    public int PaymentId { get; }
    public string ExternalPaymentId { get; }
    public PaymentStatus OldStatus { get; }
    public PaymentStatus NewStatus { get; }
    public string? TransactionHash { get; }
    public decimal? ReceivedAmount { get; }

    public CryptoPaymentStatusChangedIntegrationEvent(int paymentId, string externalPaymentId, 
        PaymentStatus oldStatus, PaymentStatus newStatus, string? transactionHash, decimal? receivedAmount)
    {
        PaymentId = paymentId;
        ExternalPaymentId = externalPaymentId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        TransactionHash = transactionHash;
        ReceivedAmount = receivedAmount;
    }
}