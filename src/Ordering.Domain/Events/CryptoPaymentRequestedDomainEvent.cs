namespace eShop.Ordering.Domain.Events;

public class CryptoPaymentRequestedDomainEvent : INotification
{
    public int OrderId { get; private set; }
    public string CryptoPaymentId { get; private set; }
    public decimal CryptoAmount { get; private set; }
    public DateTime RequestedAt { get; private set; }

    public CryptoPaymentRequestedDomainEvent(int orderId, string cryptoPaymentId, decimal cryptoAmount)
    {
        OrderId = orderId;
        CryptoPaymentId = cryptoPaymentId;
        CryptoAmount = cryptoAmount;
        RequestedAt = DateTime.UtcNow;
    }
}