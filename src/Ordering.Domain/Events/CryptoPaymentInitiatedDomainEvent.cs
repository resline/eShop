using eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

namespace eShop.Ordering.Domain.Events;

public class CryptoPaymentInitiatedDomainEvent : INotification
{
    public int OrderId { get; private set; }
    public string BuyerIdentityGuid { get; private set; }
    public CryptoCurrency Currency { get; private set; }
    public BlockchainNetwork Network { get; private set; }
    public decimal Amount { get; private set; }
    public CryptoWalletAddress PaymentAddress { get; private set; }
    public DateTime InitiatedAt { get; private set; }

    public CryptoPaymentInitiatedDomainEvent(
        int orderId, 
        string buyerIdentityGuid,
        CryptoCurrency currency,
        BlockchainNetwork network,
        decimal amount,
        CryptoWalletAddress paymentAddress)
    {
        OrderId = orderId;
        BuyerIdentityGuid = buyerIdentityGuid;
        Currency = currency;
        Network = network;
        Amount = amount;
        PaymentAddress = paymentAddress;
        InitiatedAt = DateTime.UtcNow;
    }
}