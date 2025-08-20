namespace eShop.CryptoPayment.API.IntegrationEvents.Events;

public record CryptoPaymentConfirmedIntegrationEvent : IntegrationEvent
{
    public int PaymentId { get; }
    public string ExternalPaymentId { get; }
    public string CryptoCurrency { get; }
    public decimal ReceivedAmount { get; }
    public string TransactionHash { get; }
    public int Confirmations { get; }

    public CryptoPaymentConfirmedIntegrationEvent(int paymentId, string externalPaymentId, 
        string cryptoCurrency, decimal receivedAmount, string transactionHash, int confirmations)
    {
        PaymentId = paymentId;
        ExternalPaymentId = externalPaymentId;
        CryptoCurrency = cryptoCurrency;
        ReceivedAmount = receivedAmount;
        TransactionHash = transactionHash;
        Confirmations = confirmations;
    }
}