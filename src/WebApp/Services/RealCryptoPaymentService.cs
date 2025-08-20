using System.Text.Json;
using QRCoder;
using Polly;
using Polly.Extensions.Http;

namespace eShop.WebApp.Services;

public class RealCryptoPaymentService : ICryptoPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RealCryptoPaymentService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    public RealCryptoPaymentService(HttpClient httpClient, ILogger<RealCryptoPaymentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Configure retry policy with exponential backoff
        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => !response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();
    }

    public async Task<CryptoPaymentInfo> InitiatePaymentAsync(decimal usdAmount, CryptoCurrency currency)
    {
        try
        {
            _logger.LogInformation("Initiating crypto payment for {Amount} USD in {Currency}", usdAmount, currency);

            // Step 1: Get current exchange rate
            var exchangeRate = await GetExchangeRateAsync(currency);
            var cryptoAmount = usdAmount / exchangeRate;

            // Step 2: Create payment through CryptoPayment.API
            var createPaymentRequest = new CreateCryptoPaymentRequest
            {
                Amount = usdAmount, // USD amount
                CryptoCurrency = MapCryptoCurrencyType(currency),
                ExpirationMinutes = 15,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = "WebApp",
                    ["requestedCryptoAmount"] = cryptoAmount
                }
            };

            var response = await _retryPipeline.ExecuteAsync(async (ct) =>
            {
                var json = JsonSerializer.Serialize(createPaymentRequest);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                return await _httpClient.PostAsync("/api/v1/crypto-payments", content, ct);
            }, CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create crypto payment. Status: {StatusCode}, Content: {Content}", 
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create crypto payment: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var paymentResponse = JsonSerializer.Deserialize<CryptoPaymentApiResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (paymentResponse == null)
            {
                throw new InvalidOperationException("Invalid response from crypto payment API");
            }

            _logger.LogInformation("Successfully created crypto payment with ID: {PaymentId}", paymentResponse.PaymentId);

            return new CryptoPaymentInfo
            {
                PaymentAddress = paymentResponse.PaymentAddress,
                CryptoAmount = cryptoAmount,
                ExchangeRate = exchangeRate,
                Currency = currency,
                UsdAmount = usdAmount,
                ExpiresAt = paymentResponse.ExpiresAt,
                TransactionId = paymentResponse.PaymentId
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
            _logger.LogDebug("Checking transaction status for {TransactionId}", transactionId);

            var response = await _retryPipeline.ExecuteAsync(async (ct) =>
            {
                return await _httpClient.GetAsync($"/api/v1/crypto-payments/{transactionId}", ct);
            }, CancellationToken.None);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Transaction {TransactionId} not found", transactionId);
                return CryptoTransactionStatus.Failed;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to check transaction status. Status: {StatusCode}", response.StatusCode);
                return CryptoTransactionStatus.Failed;
            }

            var content = await response.Content.ReadAsStringAsync();
            var paymentResponse = JsonSerializer.Deserialize<CryptoPaymentApiResponse>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (paymentResponse == null)
            {
                _logger.LogError("Invalid response when checking transaction status");
                return CryptoTransactionStatus.Failed;
            }

            var status = MapApiStatusToCryptoTransactionStatus(paymentResponse.Status);
            _logger.LogDebug("Transaction {TransactionId} status: {Status}", transactionId, status);

            return status;
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
            _logger.LogDebug("Getting exchange rate for {Currency}", currency);

            var response = await _retryPipeline.ExecuteAsync(async (ct) =>
            {
                var currencyType = MapCryptoCurrencyType(currency);
                return await _httpClient.GetAsync($"/api/v1/exchange-rates/{currencyType}", ct);
            }, CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get exchange rate from API, using fallback for {Currency}", currency);
                return GetFallbackExchangeRate(currency);
            }

            var content = await response.Content.ReadAsStringAsync();
            var exchangeRateResponse = JsonSerializer.Deserialize<ExchangeRateApiResponse>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (exchangeRateResponse?.UsdPrice == null)
            {
                _logger.LogWarning("Invalid exchange rate response for {Currency}, using fallback", currency);
                return GetFallbackExchangeRate(currency);
            }

            _logger.LogDebug("Got exchange rate for {Currency}: ${Rate:F2}", currency, exchangeRateResponse.UsdPrice);
            return exchangeRateResponse.UsdPrice;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exchange rate for {Currency}, using fallback", currency);
            return GetFallbackExchangeRate(currency);
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

    private static CryptoCurrencyType MapCryptoCurrencyType(CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => CryptoCurrencyType.Bitcoin,
            CryptoCurrency.Ethereum => CryptoCurrencyType.Ethereum,
            CryptoCurrency.USDT => CryptoCurrencyType.USDT,
            CryptoCurrency.USDC => CryptoCurrencyType.USDC,
            _ => throw new ArgumentException($"Unsupported currency: {currency}")
        };
    }

    private static CryptoTransactionStatus MapApiStatusToCryptoTransactionStatus(string apiStatus)
    {
        return apiStatus?.ToLowerInvariant() switch
        {
            "pending" => CryptoTransactionStatus.Pending,
            "confirmed" or "paid" => CryptoTransactionStatus.Confirmed,
            "failed" => CryptoTransactionStatus.Failed,
            "expired" => CryptoTransactionStatus.Expired,
            _ => CryptoTransactionStatus.Pending
        };
    }

    private static decimal GetFallbackExchangeRate(CryptoCurrency currency)
    {
        // Fallback exchange rates (should be updated with more recent values)
        return currency switch
        {
            CryptoCurrency.Bitcoin => 45000m,
            CryptoCurrency.Ethereum => 2800m,
            CryptoCurrency.USDT => 1m,
            CryptoCurrency.USDC => 1m,
            _ => throw new ArgumentException($"Unsupported currency: {currency}")
        };
    }
}

// API Request/Response models
public class CreateCryptoPaymentRequest
{
    public decimal Amount { get; set; }
    public CryptoCurrencyType CryptoCurrency { get; set; }
    public int? ExpirationMinutes { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class CryptoPaymentApiResponse
{
    public string PaymentId { get; set; } = string.Empty;
    public string PaymentAddress { get; set; } = string.Empty;
    public decimal RequestedAmount { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TransactionHash { get; set; }
    public int? Confirmations { get; set; }
    public int RequiredConfirmations { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ExchangeRateApiResponse
{
    public CryptoCurrencyType Currency { get; set; }
    public decimal UsdPrice { get; set; }
    public decimal PriceChangePercentage24h { get; set; }
    public DateTime LastUpdated { get; set; }
    public string Source { get; set; } = string.Empty;
}

public enum CryptoCurrencyType
{
    Bitcoin = 1,
    Ethereum = 2,
    USDT = 3,
    USDC = 4
}