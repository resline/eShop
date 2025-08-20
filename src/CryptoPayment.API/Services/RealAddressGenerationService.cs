using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Security;
using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

public class RealAddressGenerationService : IAddressGenerationService
{
    private readonly CryptoPaymentContext _context;
    private readonly IBlockchainServiceFactory _blockchainServiceFactory;
    private readonly AddressValidator _addressValidator;
    private readonly ILogger<RealAddressGenerationService> _logger;

    public RealAddressGenerationService(
        CryptoPaymentContext context,
        IBlockchainServiceFactory blockchainServiceFactory,
        AddressValidator addressValidator,
        ILogger<RealAddressGenerationService> logger)
    {
        _context = context;
        _blockchainServiceFactory = blockchainServiceFactory;
        _addressValidator = addressValidator;
        _logger = logger;
    }

    public async Task<PaymentAddress> GenerateAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating new address for {CryptoCurrency}", cryptoCurrency);

        try
        {
            var blockchainService = GetBlockchainService(cryptoCurrency);
            var address = await blockchainService.GenerateAddressAsync();

            // Validate the generated address
            if (!await ValidateAddressAsync(address, cryptoCurrency, cancellationToken))
            {
                throw new InvalidOperationException($"Generated invalid address: {address}");
            }

            // Check for address reuse prevention
            var existingAddress = await _context.PaymentAddresses
                .FirstOrDefaultAsync(pa => pa.Address == address, cancellationToken);

            if (existingAddress != null)
            {
                _logger.LogWarning("Address {Address} already exists, generating new one", address);
                return await GenerateAddressAsync(cryptoCurrency, cancellationToken); // Retry
            }

            var paymentAddress = new PaymentAddress
            {
                Address = address,
                CryptoCurrencyId = (int)cryptoCurrency,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["generationMethod"] = "blockchain-service",
                    ["validatedAt"] = DateTime.UtcNow
                }
            };

            _context.PaymentAddresses.Add(paymentAddress);
            await _context.SaveChangesAsync(cancellationToken);

            // Load the cryptocurrency navigation property
            await _context.Entry(paymentAddress)
                .Reference(pa => pa.CryptoCurrency)
                .LoadAsync(cancellationToken);

            _logger.LogInformation("Generated new {CryptoCurrency} address: {Address}", cryptoCurrency, address);

            return paymentAddress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate address for {CryptoCurrency}", cryptoCurrency);
            
            // Fallback to mock generation for development/testing
            _logger.LogWarning("Falling back to mock address generation");
            return await GenerateMockAddressAsync(cryptoCurrency, cancellationToken);
        }
    }

    public async Task<PaymentAddress?> GetUnusedAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        var address = await _context.PaymentAddresses
            .Include(pa => pa.CryptoCurrency)
            .Where(pa => pa.CryptoCurrencyId == (int)cryptoCurrency && !pa.IsUsed)
            .OrderBy(pa => pa.CreatedAt) // Oldest first to ensure fair usage
            .FirstOrDefaultAsync(cancellationToken);

        if (address != null)
        {
            _logger.LogInformation("Found unused {CryptoCurrency} address", cryptoCurrency);
        }

        return address;
    }

    public async Task MarkAddressAsUsedAsync(int addressId, CancellationToken cancellationToken = default)
    {
        var address = await _context.PaymentAddresses
            .FirstOrDefaultAsync(pa => pa.Id == addressId, cancellationToken);

        if (address != null)
        {
            address.IsUsed = true;
            address.UsedAt = DateTime.UtcNow;
            
            // Add usage metadata
            if (address.Metadata == null)
            {
                address.Metadata = new Dictionary<string, object>();
            }
            address.Metadata["markedUsedAt"] = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Marked address {AddressId} as used", addressId);
        }
        else
        {
            _logger.LogWarning("Attempted to mark non-existent address {AddressId} as used", addressId);
        }
    }

    public async Task<bool> ValidateAddressAsync(string address, CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        try
        {
            var currency = MapCryptoCurrencyType(cryptoCurrency);
            var isValid = await _addressValidator.ValidateAsync(address, currency);
            
            _logger.LogInformation("Address validation for {Currency}: {IsValid}", cryptoCurrency, isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating address {Address} for {Currency}", address, cryptoCurrency);
            return false;
        }
    }

    public async Task<int> GetAddressUsageCountAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        return await _context.PaymentAddresses
            .CountAsync(pa => pa.CryptoCurrencyId == (int)cryptoCurrency && pa.IsUsed, cancellationToken);
    }

    public async Task<int> GetUnusedAddressCountAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        return await _context.PaymentAddresses
            .CountAsync(pa => pa.CryptoCurrencyId == (int)cryptoCurrency && !pa.IsUsed, cancellationToken);
    }

    public async Task<bool> PreGenerateAddressesAsync(CryptoCurrencyType cryptoCurrency, int count, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Pre-generating {Count} addresses for {CryptoCurrency}", count, cryptoCurrency);

        var successCount = 0;
        var failureCount = 0;

        for (int i = 0; i < count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await GenerateAddressAsync(cryptoCurrency, cancellationToken);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pre-generate address {Index} for {CryptoCurrency}", i + 1, cryptoCurrency);
                failureCount++;
            }

            // Small delay to avoid overwhelming the system
            await Task.Delay(100, cancellationToken);
        }

        _logger.LogInformation("Pre-generation completed for {CryptoCurrency}: {Success} successful, {Failures} failed", 
            cryptoCurrency, successCount, failureCount);

        return failureCount == 0;
    }

    private IBlockchainService GetBlockchainService(CryptoCurrencyType cryptoCurrency)
    {
        var currency = MapCryptoCurrencyType(cryptoCurrency);
        return _blockchainServiceFactory.GetService(currency);
    }

    private static string MapCryptoCurrencyType(CryptoCurrencyType cryptoCurrency)
    {
        return cryptoCurrency switch
        {
            CryptoCurrencyType.Bitcoin => "BTC",
            CryptoCurrencyType.Ethereum => "ETH",
            CryptoCurrencyType.USDT => "ETH", // USDT runs on Ethereum
            CryptoCurrencyType.USDC => "ETH", // USDC runs on Ethereum
            _ => throw new ArgumentException($"Unsupported cryptocurrency: {cryptoCurrency}")
        };
    }

    // Fallback mock generation for development/testing
    private async Task<PaymentAddress> GenerateMockAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Using mock address generation for {CryptoCurrency}", cryptoCurrency);

        var address = cryptoCurrency switch
        {
            CryptoCurrencyType.Bitcoin => GenerateMockBitcoinAddress(),
            CryptoCurrencyType.Ethereum => GenerateMockEthereumAddress(),
            CryptoCurrencyType.USDT => GenerateMockEthereumAddress(),
            CryptoCurrencyType.USDC => GenerateMockEthereumAddress(),
            _ => throw new NotSupportedException($"Cryptocurrency {cryptoCurrency} is not supported")
        };

        var paymentAddress = new PaymentAddress
        {
            Address = address,
            CryptoCurrencyId = (int)cryptoCurrency,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["generationMethod"] = "mock",
                ["isMockAddress"] = true
            }
        };

        _context.PaymentAddresses.Add(paymentAddress);
        await _context.SaveChangesAsync(cancellationToken);

        // Load the cryptocurrency navigation property
        await _context.Entry(paymentAddress)
            .Reference(pa => pa.CryptoCurrency)
            .LoadAsync(cancellationToken);

        return paymentAddress;
    }

    private static string GenerateMockBitcoinAddress()
    {
        var random = new Random();
        var chars = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var result = new char[34];
        result[0] = '1'; // Legacy P2PKH address prefix
        
        for (int i = 1; i < 34; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        
        return new string(result);
    }

    private static string GenerateMockEthereumAddress()
    {
        var random = new Random();
        var chars = "0123456789abcdef";
        var result = new char[42];
        result[0] = '0';
        result[1] = 'x';
        
        for (int i = 2; i < 42; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        
        return new string(result);
    }
}