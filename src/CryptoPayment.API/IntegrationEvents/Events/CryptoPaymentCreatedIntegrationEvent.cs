namespace eShop.CryptoPayment.API.IntegrationEvents.Events;

public record CryptoPaymentCreatedIntegrationEvent : IntegrationEvent
{
    public int PaymentId { get; }
    public string ExternalPaymentId { get; }
    public string CryptoCurrency { get; }
    public string PaymentAddress { get; }
    public decimal RequestedAmount { get; }

    public CryptoPaymentCreatedIntegrationEvent(int paymentId, string externalPaymentId, 
        string cryptoCurrency, string paymentAddress, decimal requestedAmount)
    {
        PaymentId = paymentId;
        ExternalPaymentId = externalPaymentId;
        CryptoCurrency = cryptoCurrency;
        PaymentAddress = paymentAddress;
        RequestedAmount = requestedAmount;
    }
}