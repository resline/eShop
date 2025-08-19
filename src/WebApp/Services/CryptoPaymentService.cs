using System.Text.Json;
using QRCoder;

namespace eShop.WebApp.Services;

public interface ICryptoPaymentService
{
    Task<CryptoPaymentInfo> InitiatePaymentAsync(decimal usdAmount, CryptoCurrency currency);
    Task<CryptoTransactionStatus> CheckTransactionStatusAsync(string transactionId);
    Task<decimal> GetExchangeRateAsync(CryptoCurrency currency);
    Task<byte[]> GenerateQRCodeAsync(string paymentUri);
    string GetPaymentUri(CryptoCurrency currency, string address, decimal amount);
    string GetCurrencySymbol(CryptoCurrency currency);
    string GetCurrencyName(CryptoCurrency currency);
}

public class CryptoPaymentService : ICryptoPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CryptoPaymentService> _logger;

    public CryptoPaymentService(HttpClient httpClient, ILogger<CryptoPaymentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CryptoPaymentInfo> InitiatePaymentAsync(decimal usdAmount, CryptoCurrency currency)
    {
        try
        {
            // Simulate API call to crypto payment processor
            var exchangeRate = await GetExchangeRateAsync(currency);
            var cryptoAmount = usdAmount / exchangeRate;

            // Generate a mock payment address (in real implementation, this would come from the payment processor)
            var address = GenerateMockAddress(currency);

            return new CryptoPaymentInfo
            {
                PaymentAddress = address,
                CryptoAmount = cryptoAmount,
                ExchangeRate = exchangeRate,
                Currency = currency,
                UsdAmount = usdAmount,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15), // 15-minute expiry
                TransactionId = Guid.NewGuid().ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating crypto payment for {Currency}", currency);
            throw;
        }
    }

    public async Task<CryptoTransactionStatus> CheckTransactionStatusAsync(string transactionId)
    {
        try
        {
            // Simulate API call to check transaction status
            await Task.Delay(100); // Simulate network delay
            
            // For demo purposes, randomly return status
            var random = new Random();
            var statusValue = random.Next(0, 4);
            return (CryptoTransactionStatus)statusValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking transaction status for {TransactionId}", transactionId);
            return CryptoTransactionStatus.Failed;
        }
    }

    public async Task<decimal> GetExchangeRateAsync(CryptoCurrency currency)
    {
        try
        {
            // Simulate API call to get current exchange rates
            await Task.Delay(50); // Simulate network delay

            // Mock exchange rates (in real implementation, fetch from a reliable API)
            return currency switch
            {
                CryptoCurrency.Bitcoin => 45000m,
                CryptoCurrency.Ethereum => 2800m,
                CryptoCurrency.USDT => 1m,
                CryptoCurrency.USDC => 1m,
                _ => throw new ArgumentException($"Unsupported currency: {currency}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exchange rate for {Currency}", currency);
            throw;
        }
    }

    public async Task<byte[]> GenerateQRCodeAsync(string paymentUri)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(paymentUri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            
            return await Task.FromResult(qrCode.GetGraphic(20));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR code for payment URI: {PaymentUri}", paymentUri);
            throw;
        }
    }

    public string GetPaymentUri(CryptoCurrency currency, string address, decimal amount)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => $"bitcoin:{address}?amount={amount:F8}",
            CryptoCurrency.Ethereum => $"ethereum:{address}?value={amount:F18}",
            CryptoCurrency.USDT => $"ethereum:{address}?value={amount:F6}",
            CryptoCurrency.USDC => $"ethereum:{address}?value={amount:F6}",
            _ => throw new ArgumentException($"Unsupported currency: {currency}")
        };
    }

    public string GetCurrencySymbol(CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => "BTC",
            CryptoCurrency.Ethereum => "ETH",
            CryptoCurrency.USDT => "USDT",
            CryptoCurrency.USDC => "USDC",
            _ => throw new ArgumentException($"Unsupported currency: {currency}")
        };
    }

    public string GetCurrencyName(CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => "Bitcoin",
            CryptoCurrency.Ethereum => "Ethereum",
            CryptoCurrency.USDT => "Tether",
            CryptoCurrency.USDC => "USD Coin",
            _ => throw new ArgumentException($"Unsupported currency: {currency}")
        };
    }

    private string GenerateMockAddress(CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
            CryptoCurrency.Ethereum => "0x742d35Cc6634C0532925a3b8D94d7CAa2F6C7F8E",
            CryptoCurrency.USDT => "0x742d35Cc6634C0532925a3b8D94d7CAa2F6C7F8E",
            CryptoCurrency.USDC => "0x742d35Cc6634C0532925a3b8D94d7CAa2F6C7F8E",
            _ => throw new ArgumentException($"Unsupported currency: {currency}")
        };
    }
}

public class CryptoPaymentInfo
{
    public string PaymentAddress { get; set; } = string.Empty;
    public decimal CryptoAmount { get; set; }
    public decimal ExchangeRate { get; set; }
    public CryptoCurrency Currency { get; set; }
    public decimal UsdAmount { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}