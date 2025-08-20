using System.Numerics;

namespace CryptoPayment.BlockchainServices.Models;

public record Transaction
{
    public string Id { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string ToAddress { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? TokenContract { get; init; }
    public TransactionStatus Status { get; init; }
    public int Confirmations { get; init; }
    public int RequiredConfirmations { get; init; }
    public decimal Fee { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public string? BlockHash { get; init; }
    public long? BlockNumber { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public record PaymentAddress
{
    public string Address { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string? TokenContract { get; init; }
    public string PrivateKeyId { get; init; } = string.Empty; // Reference to encrypted key, not the actual key
    public string PublicKey { get; init; } = string.Empty;
    public int DerivationIndex { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public bool IsActive { get; init; } = true;
    public string? Label { get; init; }
}

public record TransactionRequest
{
    public string ToAddress { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? TokenContract { get; init; }
    public string? FromAddress { get; init; }
    public string? PrivateKeyId { get; init; } // Reference to stored key
    public decimal? GasPrice { get; init; }
    public BigInteger? GasLimit { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public record TransactionResult
{
    public bool Success { get; init; }
    public string? TransactionHash { get; init; }
    public string? Error { get; init; }
    public decimal EstimatedFee { get; init; }
    public Transaction? Transaction { get; init; }
}

public record BlockchainInfo
{
    public string Network { get; init; } = string.Empty;
    public long BlockHeight { get; init; }
    public string BlockHash { get; init; } = string.Empty;
    public decimal NetworkFee { get; init; }
    public bool IsConnected { get; init; }
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

public record AddressBalance
{
    public string Address { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? TokenContract { get; init; }
    public decimal ConfirmedBalance { get; init; }
    public decimal UnconfirmedBalance { get; init; }
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

public record GasEstimate
{
    public BigInteger GasLimit { get; init; }
    public BigInteger GasPrice { get; init; }
    public BigInteger MaxFeePerGas { get; init; }
    public BigInteger MaxPriorityFeePerGas { get; init; }
    public decimal EstimatedCost { get; init; }
    public string Currency { get; init; } = "ETH";
}

public enum TransactionStatus
{
    Pending,
    Broadcasting,
    Mempool,
    Confirming,
    Confirmed,
    Failed,
    Rejected,
    Expired
}

public enum CryptoCurrency
{
    BTC,
    ETH,
    USDT,
    USDC
}

public static class CurrencyExtensions
{
    public static string ToSymbol(this CryptoCurrency currency) => currency.ToString();
    
    public static bool IsToken(this CryptoCurrency currency) => currency is CryptoCurrency.USDT or CryptoCurrency.USDC;
    
    public static string GetTokenContract(this CryptoCurrency currency, NetworkType network = NetworkType.Mainnet)
    {
        return currency switch
        {
            CryptoCurrency.USDT when network == NetworkType.Mainnet => "0xdAC17F958D2ee523a2206206994597C13D831ec7",
            CryptoCurrency.USDC when network == NetworkType.Mainnet => "0xA0b86a33E6b7Cf5870b37b7c7d5D6de3E34B24E8",
            CryptoCurrency.USDT when network == NetworkType.Testnet => "0x110a13FC3efE6A245B50102D2d79B3E76125Ae83",
            CryptoCurrency.USDC when network == NetworkType.Testnet => "0x07865c6E87B9F70255377e024ace6630C1Eaa37F",
            _ => string.Empty
        };
    }
}