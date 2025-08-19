using System.Text.RegularExpressions;
using eShop.Ordering.Domain.SeedWork;
using eShop.Ordering.Domain.Exceptions;

namespace eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

public class CryptoWalletAddress : ValueObject
{
    public string Address { get; private set; }
    public BlockchainNetwork Network { get; private set; }
    public string? Label { get; private set; }

    // Bitcoin address patterns
    private static readonly Regex BitcoinP2PKHRegex = new(@"^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$", RegexOptions.Compiled);
    private static readonly Regex BitcoinBech32Regex = new(@"^(bc1|[13])[a-zA-HJ-NP-Z0-9]{25,87}$", RegexOptions.Compiled);
    
    // Ethereum address pattern (including EVM-compatible chains)
    private static readonly Regex EthereumAddressRegex = new(@"^0x[a-fA-F0-9]{40}$", RegexOptions.Compiled);

    protected CryptoWalletAddress() { }

    public CryptoWalletAddress(string address, BlockchainNetwork network, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new OrderingDomainException("Crypto wallet address cannot be empty");

        if (network == null)
            throw new OrderingDomainException("Blockchain network cannot be null");

        if (!IsValidAddressForNetwork(address, network))
            throw new OrderingDomainException($"Invalid address format for {network.Name} network: {address}");

        Address = address.Trim();
        Network = network;
        Label = label?.Trim();
    }

    private static bool IsValidAddressForNetwork(string address, BlockchainNetwork network)
    {
        return network.Name switch
        {
            "Bitcoin" => IsValidBitcoinAddress(address),
            "Ethereum" => IsValidEthereumAddress(address),
            "Polygon" => IsValidEthereumAddress(address), // EVM-compatible
            "Binance Smart Chain" => IsValidEthereumAddress(address), // EVM-compatible
            "Ethereum Goerli" => IsValidEthereumAddress(address), // EVM-compatible
            _ => false
        };
    }

    private static bool IsValidBitcoinAddress(string address)
    {
        return BitcoinP2PKHRegex.IsMatch(address) || BitcoinBech32Regex.IsMatch(address);
    }

    private static bool IsValidEthereumAddress(string address)
    {
        return EthereumAddressRegex.IsMatch(address);
    }

    public bool IsEqualTo(string address, BlockchainNetwork network)
    {
        return Address.Equals(address, StringComparison.OrdinalIgnoreCase) 
               && Network.Equals(network);
    }

    public CryptoWalletAddress WithLabel(string? newLabel)
    {
        return new CryptoWalletAddress(Address, Network, newLabel);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Address.ToLowerInvariant();
        yield return Network;
    }

    public override string ToString()
    {
        var labelPart = !string.IsNullOrWhiteSpace(Label) ? $" ({Label})" : "";
        return $"{Address} [{Network.Name}]{labelPart}";
    }
}