namespace eShop.CryptoPayment.API.Services;

public class AddressGenerationService : IAddressGenerationService
{
    private readonly CryptoPaymentContext _context;
    private readonly ILogger<AddressGenerationService> _logger;
    private readonly Random _random = new();

    public AddressGenerationService(CryptoPaymentContext context, ILogger<AddressGenerationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PaymentAddress> GenerateAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating new address for {CryptoCurrency}", cryptoCurrency);

        var address = cryptoCurrency switch
        {
            CryptoCurrencyType.Bitcoin => GenerateBitcoinAddress(),
            CryptoCurrencyType.Ethereum => GenerateEthereumAddress(),
            _ => throw new NotSupportedException($"Cryptocurrency {cryptoCurrency} is not supported")
        };

        var paymentAddress = new PaymentAddress
        {
            Address = address,
            CryptoCurrencyId = (int)cryptoCurrency,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
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

    public async Task<PaymentAddress?> GetUnusedAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        var address = await _context.PaymentAddresses
            .Include(pa => pa.CryptoCurrency)
            .FirstOrDefaultAsync(pa => pa.CryptoCurrencyId == (int)cryptoCurrency && !pa.IsUsed, cancellationToken);

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
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ValidateAddressAsync(string address, CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Placeholder for async signature

        return cryptoCurrency switch
        {
            CryptoCurrencyType.Bitcoin => ValidateBitcoinAddress(address),
            CryptoCurrencyType.Ethereum => ValidateEthereumAddress(address),
            _ => false
        };
    }

    private string GenerateBitcoinAddress()
    {
        // This is a simplified mock implementation
        // In production, use proper Bitcoin address generation with private keys
        var chars = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var result = new char[34];
        result[0] = '1'; // Legacy P2PKH address prefix
        
        for (int i = 1; i < 34; i++)
        {
            result[i] = chars[_random.Next(chars.Length)];
        }
        
        return new string(result);
    }

    private string GenerateEthereumAddress()
    {
        // This is a simplified mock implementation
        // In production, use proper Ethereum address generation with private keys
        var chars = "0123456789abcdef";
        var result = new char[42];
        result[0] = '0';
        result[1] = 'x';
        
        for (int i = 2; i < 42; i++)
        {
            result[i] = chars[_random.Next(chars.Length)];
        }
        
        return new string(result);
    }

    private static bool ValidateBitcoinAddress(string address)
    {
        // Basic validation - in production use proper Bitcoin address validation
        return !string.IsNullOrEmpty(address) && 
               address.Length >= 26 && 
               address.Length <= 35 && 
               (address.StartsWith('1') || address.StartsWith('3') || address.StartsWith("bc1"));
    }

    private static bool ValidateEthereumAddress(string address)
    {
        // Basic validation - in production use proper Ethereum address validation
        return !string.IsNullOrEmpty(address) && 
               address.Length == 42 && 
               address.StartsWith("0x") &&
               address[2..].All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }
}