using eShop.Ordering.Domain.SeedWork;

namespace eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

public sealed class BlockchainNetwork : ValueObject
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string ChainId { get; init; }
    public required string NativeCurrency { get; init; }
    public bool IsTestnet { get; init; }

    public static readonly BlockchainNetwork Bitcoin = new() 
    { 
        Id = 1, 
        Name = "Bitcoin", 
        ChainId = "mainnet", 
        NativeCurrency = "BTC", 
        IsTestnet = false 
    };

    public static readonly BlockchainNetwork Ethereum = new() 
    { 
        Id = 2, 
        Name = "Ethereum", 
        ChainId = "1", 
        NativeCurrency = "ETH", 
        IsTestnet = false 
    };

    public static readonly BlockchainNetwork Polygon = new() 
    { 
        Id = 3, 
        Name = "Polygon", 
        ChainId = "137", 
        NativeCurrency = "MATIC", 
        IsTestnet = false 
    };

    public static readonly BlockchainNetwork BSC = new() 
    { 
        Id = 4, 
        Name = "Binance Smart Chain", 
        ChainId = "56", 
        NativeCurrency = "BNB", 
        IsTestnet = false 
    };

    // Testnets
    public static readonly BlockchainNetwork EthereumGoerli = new() 
    { 
        Id = 5, 
        Name = "Ethereum Goerli", 
        ChainId = "5", 
        NativeCurrency = "ETH", 
        IsTestnet = true 
    };

    public static IEnumerable<BlockchainNetwork> SupportedNetworks =>
        new[] { Bitcoin, Ethereum, Polygon, BSC, EthereumGoerli };

    public static IEnumerable<BlockchainNetwork> MainnetNetworks =>
        SupportedNetworks.Where(n => !n.IsTestnet);

    public static IEnumerable<BlockchainNetwork> TestnetNetworks =>
        SupportedNetworks.Where(n => n.IsTestnet);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Id;
        yield return Name;
        yield return ChainId;
    }

    public static BlockchainNetwork FromId(int id)
    {
        return SupportedNetworks.SingleOrDefault(n => n.Id == id)
            ?? throw new ArgumentException($"Unsupported blockchain network with ID: {id}");
    }

    public static BlockchainNetwork FromChainId(string chainId)
    {
        return SupportedNetworks.SingleOrDefault(n => n.ChainId.Equals(chainId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unsupported blockchain network with chain ID: {chainId}");
    }
}