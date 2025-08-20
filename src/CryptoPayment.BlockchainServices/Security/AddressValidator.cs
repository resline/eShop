using System.Text.RegularExpressions;
using NBitcoin;
using Nethereum.Util;
using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.BlockchainServices.Security;

public partial class AddressValidator : IAddressValidator
{
    private readonly ILogger<AddressValidator> _logger;
    private static readonly AddressUtil _ethereumAddressUtil = new();

    public AddressValidator(ILogger<AddressValidator> logger)
    {
        _logger = logger;
    }

    public bool IsValid(string address, string currency)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(currency))
        {
            return false;
        }

        try
        {
            return currency.ToUpperInvariant() switch
            {
                "BTC" => IsValidBitcoinAddress(address),
                "ETH" or "USDT" or "USDC" => IsValidEthereumAddress(address),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating address for currency {Currency}", currency);
            return false;
        }
    }

    public bool IsValidBitcoinAddress(string address, NetworkType network = NetworkType.Testnet)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            var bitcoinNetwork = network == NetworkType.Mainnet ? Network.Main : Network.TestNet;
            
            // Try to parse as Bitcoin address
            var bitcoinAddress = BitcoinAddress.Create(address, bitcoinNetwork);
            
            // Additional validation based on address type
            return address.Length switch
            {
                // Legacy address (P2PKH)
                >= 26 and <= 35 when address.StartsWith('1') || address.StartsWith('m') || address.StartsWith('n') => true,
                
                // Script address (P2SH)
                >= 26 and <= 35 when address.StartsWith('3') || address.StartsWith('2') => true,
                
                // Bech32 address (P2WPKH, P2WSH)
                >= 42 and <= 62 when address.StartsWith("bc1") || address.StartsWith("tb1") => IsValidBech32Address(address),
                
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Invalid Bitcoin address format detected");
            return false;
        }
    }

    public bool IsValidEthereumAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            // Remove 0x prefix if present
            if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                address = address[2..];
            }

            // Check length (20 bytes = 40 hex characters)
            if (address.Length != 40)
            {
                return false;
            }

            // Check if it's valid hex
            if (!IsValidHex(address))
            {
                return false;
            }

            // Use Nethereum's built-in validation
            return _ethereumAddressUtil.IsValidEthereumAddressHexFormat("0x" + address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Invalid Ethereum address format detected");
            return false;
        }
    }

    public bool IsValidTokenContract(string contractAddress)
    {
        // Token contracts use the same address format as Ethereum addresses
        return IsValidEthereumAddress(contractAddress);
    }

    private bool IsValidBech32Address(string address)
    {
        try
        {
            // Basic Bech32 validation
            if (!Bech32Regex().IsMatch(address))
            {
                return false;
            }

            // Split address into human-readable part and data part
            var lastSeparator = address.LastIndexOf('1');
            if (lastSeparator == -1 || lastSeparator == 0 || lastSeparator + 7 > address.Length)
            {
                return false;
            }

            var hrp = address[..lastSeparator];
            var data = address[(lastSeparator + 1)..];

            // Validate human-readable part
            if (!IsValidBech32Hrp(hrp))
            {
                return false;
            }

            // Validate data part
            return IsValidBech32Data(data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Invalid Bech32 address format detected");
            return false;
        }
    }

    private static bool IsValidBech32Hrp(string hrp)
    {
        // Human-readable part should be "bc" for mainnet or "tb" for testnet
        return hrp is "bc" or "tb";
    }

    private static bool IsValidBech32Data(string data)
    {
        // Bech32 data part should only contain valid characters
        const string bech32Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        
        foreach (var c in data.ToLowerInvariant())
        {
            if (!bech32Charset.Contains(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidHex(string hex)
    {
        return HexRegex().IsMatch(hex);
    }

    [GeneratedRegex(@"^[a-fA-F0-9]+$")]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"^(bc|tb)1[a-z0-9]{39,59}$")]
    private static partial Regex Bech32Regex();
}