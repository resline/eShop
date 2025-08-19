namespace eShop.Ordering.Domain.Events;

public class CryptoPaymentConfirmedDomainEvent : INotification
{
    public int OrderId { get; private set; }
    public string CryptoPaymentId { get; private set; }
    public string TransactionHash { get; private set; }
    public decimal CryptoAmount { get; private set; }
    public DateTime ConfirmedAt { get; private set; }

    public CryptoPaymentConfirmedDomainEvent(int orderId, string cryptoPaymentId, string transactionHash, decimal cryptoAmount)
    {
        OrderId = orderId;
        CryptoPaymentId = cryptoPaymentId;
        TransactionHash = transactionHash;
        CryptoAmount = cryptoAmount;
        ConfirmedAt = DateTime.UtcNow;
    }
}