namespace CryptoPayment.BlockchainServices.Configuration;

public class BlockchainOptions
{
    public const string SectionName = "Blockchain";

    public BitcoinOptions Bitcoin { get; set; } = new();
    public EthereumOptions Ethereum { get; set; } = new();
    public ProvidersOptions Providers { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public MonitoringOptions Monitoring { get; set; } = new();
}

public class BitcoinOptions
{
    [Required]
    public string RpcEndpoint { get; set; } = string.Empty;
    
    public string? RpcUsername { get; set; }
    
    public string? RpcPassword { get; set; }
    
    public NetworkType Network { get; set; } = NetworkType.Testnet;
    
    public int ConfirmationsRequired { get; set; } = 3;
    
    public decimal MinimumAmount { get; set; } = 0.0001m;
    
    public int HDWalletAccountIndex { get; set; } = 0;
    
    public TimeSpan TransactionTimeout { get; set; } = TimeSpan.FromMinutes(30);
}

public class EthereumOptions
{
    [Required]
    public string RpcEndpoint { get; set; } = string.Empty;
    
    public string? InfuraProjectId { get; set; }
    
    public string? AlchemyApiKey { get; set; }
    
    public NetworkType Network { get; set; } = NetworkType.Testnet;
    
    public int ConfirmationsRequired { get; set; } = 12;
    
    public decimal MinimumAmount { get; set; } = 0.001m;
    
    public BigInteger GasLimit { get; set; } = 21000;
    
    public BigInteger MaxFeePerGas { get; set; } = 20000000000; // 20 Gwei
    
    public BigInteger MaxPriorityFeePerGas { get; set; } = 2000000000; // 2 Gwei
    
    public Dictionary<string, string> TokenContracts { get; set; } = new()
    {
        ["USDT"] = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
        ["USDC"] = "0xA0b86a33E6b7Cf5870b37b7c7d5D6de3E34B24E8"
    };
    
    public TimeSpan TransactionTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

public class ProvidersOptions
{
    public CoinbaseCommerceOptions CoinbaseCommerce { get; set; } = new();
    public BitPayOptions BitPay { get; set; } = new();
}

public class CoinbaseCommerceOptions
{
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string BaseUrl { get; set; } = "https://api.commerce.coinbase.com";
    public bool Enabled { get; set; } = false;
}

public class BitPayOptions
{
    public string? ApiToken { get; set; }
    public string? PrivateKey { get; set; }
    public string BaseUrl { get; set; } = "https://test.bitpay.com";
    public bool Enabled { get; set; } = false;
}

public class SecurityOptions
{
    public string EncryptionKey { get; set; } = string.Empty;
    public KeyStorageType KeyStorageType { get; set; } = KeyStorageType.InMemory;
    public string? KeyStoragePath { get; set; }
    public bool ValidateAddresses { get; set; } = true;
    public bool RequireSecureConnection { get; set; } = true;
}

public class MonitoringOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan WebSocketReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; set; } = 10;
    public bool EnableWebSocketMonitoring { get; set; } = true;
    public bool EnablePollingFallback { get; set; } = true;
    public TimeSpan TransactionExpiryTime { get; set; } = TimeSpan.FromHours(24);
}

public enum NetworkType
{
    Mainnet,
    Testnet
}

public enum KeyStorageType
{
    InMemory,
    File,
    AzureKeyVault,
    HashiCorpVault
}