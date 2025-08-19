using eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

namespace eShop.Ordering.Domain.Events;

public class CryptoPaymentAddressGeneratedDomainEvent : INotification
{
    public int OrderId { get; private set; }
    public string CryptoPaymentId { get; private set; }
    public CryptoWalletAddress GeneratedAddress { get; private set; }
    public CryptoCurrency Currency { get; private set; }
    public BlockchainNetwork Network { get; private set; }
    public decimal ExpectedAmount { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime GeneratedAt { get; private set; }

    public CryptoPaymentAddressGeneratedDomainEvent(
        int orderId,
        string cryptoPaymentId,
        CryptoWalletAddress generatedAddress,
        CryptoCurrency currency,
        BlockchainNetwork network,
        decimal expectedAmount,
        DateTime expiresAt)
    {
        OrderId = orderId;
        CryptoPaymentId = cryptoPaymentId;
        GeneratedAddress = generatedAddress;
        Currency = currency;
        Network = network;
        ExpectedAmount = expectedAmount;
        ExpiresAt = expiresAt;
        GeneratedAt = DateTime.UtcNow;
    }
}