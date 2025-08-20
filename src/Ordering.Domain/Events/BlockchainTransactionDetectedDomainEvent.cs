using eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

namespace eShop.Ordering.Domain.Events;

public class BlockchainTransactionDetectedDomainEvent : INotification
{
    public string TransactionHash { get; private set; }
    public CryptoWalletAddress FromAddress { get; private set; }
    public CryptoWalletAddress ToAddress { get; private set; }
    public decimal Amount { get; private set; }
    public CryptoCurrency Currency { get; private set; }
    public BlockchainNetwork Network { get; private set; }
    public int ConfirmationCount { get; private set; }
    public bool IsConfirmed { get; private set; }
    public DateTime BlockTimestamp { get; private set; }
    public DateTime DetectedAt { get; private set; }
    public string? OrderReference { get; private set; }

    public BlockchainTransactionDetectedDomainEvent(
        string transactionHash,
        CryptoWalletAddress fromAddress,
        CryptoWalletAddress toAddress,
        decimal amount,
        CryptoCurrency currency,
        BlockchainNetwork network,
        int confirmationCount,
        DateTime blockTimestamp,
        string? orderReference = null)
    {
        TransactionHash = transactionHash;
        FromAddress = fromAddress;
        ToAddress = toAddress;
        Amount = amount;
        Currency = currency;
        Network = network;
        ConfirmationCount = confirmationCount;
        IsConfirmed = confirmationCount >= GetRequiredConfirmations(network);
        BlockTimestamp = blockTimestamp;
        DetectedAt = DateTime.UtcNow;
        OrderReference = orderReference;
    }

    private static int GetRequiredConfirmations(BlockchainNetwork network)
    {
        return network.Name switch
        {
            "Bitcoin" => 6,
            "Ethereum" => 12,
            "Polygon" => 20,
            "Binance Smart Chain" => 15,
            _ => 12 // Default for EVM chains
        };
    }
}