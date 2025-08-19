using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Polly;
using Polly.Extensions.Http;
using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _httpClient;
    private readonly IDistributedCache _cache;
    private readonly ExchangeRateOptions _options;
    private readonly ILogger<ExchangeRateService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    private static readonly Dictionary<CryptoCurrencyType, string> CoinGeckoIds = new()
    {
        [CryptoCurrencyType.Bitcoin] = "bitcoin",
        [CryptoCurrencyType.Ethereum] = "ethereum",
        [CryptoCurrencyType.USDT] = "tether",
        [CryptoCurrencyType.USDC] = "usd-coin"
    };

    public ExchangeRateService(
        HttpClient httpClient, 
        IDistributedCache cache,
        ExchangeRateOptions options,
        ILogger<ExchangeRateService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options;
        _logger = logger;
        
        _httpClient.Timeout = _options.RequestTimeout;
        
        // Configure retry policy
        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                MaxRetryAttempts = _options.MaxRetries,
                Delay = _options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();
    }

    public async Task<ExchangeRateResult> GetExchangeRateAsync(CryptoCurrencyType currency, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"exchange_rate:{currency}";
        
        // Try to get from cache first
        var cachedResult = await GetFromCacheAsync(cacheKey, cancellationToken);
        if (cachedResult != null)
        {
            _logger.LogDebug("Retrieved exchange rate for {Currency} from cache", currency);
            return cachedResult;
        }

        // Fetch from API
        try
        {
            var result = await FetchExchangeRateFromCoinGeckoAsync(currency, cancellationToken);
            
            // Cache the result
            await CacheResultAsync(cacheKey, result, cancellationToken);
            
            _logger.LogInformation("Fetched exchange rate for {Currency}: ${Price:F2}", currency, result.UsdPrice);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch from CoinGecko, trying backup API for {Currency}", currency);
            
            try
            {
                var result = await FetchExchangeRateFromCoinMarketCapAsync(currency, cancellationToken);
                await CacheResultAsync(cacheKey, result, cancellationToken);
                return result;
            }
            catch (Exception backupEx)
            {
                _logger.LogError(backupEx, "All exchange rate APIs failed for {Currency}", currency);
                throw new InvalidOperationException($"Unable to fetch exchange rate for {currency}", backupEx);
            }
        }
    }

    public async Task<IReadOnlyDictionary<CryptoCurrencyType, ExchangeRateResult>> GetExchangeRatesAsync(
        IEnumerable<CryptoCurrencyType> currencies, 
        CancellationToken cancellationToken = default)
    {
        var tasks = currencies.Select(async currency => 
        {
            try
            {
                var rate = await GetExchangeRateAsync(currency, cancellationToken);
                return new KeyValuePair<CryptoCurrencyType, ExchangeRateResult>(currency, rate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get exchange rate for {Currency}", currency);
                return new KeyValuePair<CryptoCurrencyType, ExchangeRateResult>(currency, null!);
            }
        });

        var results = await Task.WhenAll(tasks);
        return results
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Test with Bitcoin as it's most stable
            await GetExchangeRateAsync(CryptoCurrencyType.Bitcoin, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ExchangeRateResult?> GetFromCacheAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                return JsonSerializer.Deserialize<ExchangeRateResult>(cachedJson);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve from cache: {CacheKey}", cacheKey);
        }
        
        return null;
    }

    private async Task CacheResultAsync(string cacheKey, ExchangeRateResult result, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(result);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.CacheExpiry
            };
            
            await _cache.SetStringAsync(cacheKey, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache result: {CacheKey}", cacheKey);
        }
    }

    private async Task<ExchangeRateResult> FetchExchangeRateFromCoinGeckoAsync(
        CryptoCurrencyType currency, 
        CancellationToken cancellationToken)
    {
        if (!CoinGeckoIds.TryGetValue(currency, out var coinId))
        {
            throw new ArgumentException($"Unsupported currency: {currency}");
        }

        var url = $"{_options.PrimaryApiUrl}/simple/price?ids={coinId}&vs_currencies=usd&include_24hr_change=true";
        
        var response = await _retryPipeline.ExecuteAsync(async (ct) =>
        {
            return await _httpClient.GetAsync(url, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<JsonElement>(content);

        if (!data.TryGetProperty(coinId, out var coinData))
        {
            throw new InvalidOperationException($"No data found for {currency}");
        }

        var price = coinData.GetProperty("usd").GetDecimal();
        var priceChange = coinData.TryGetProperty("usd_24h_change", out var changeElement) 
            ? changeElement.GetDecimal() 
            : 0m;

        return new ExchangeRateResult(
            currency,
            price,
            priceChange,
            DateTime.UtcNow,
            "CoinGecko"
        );
    }

    private async Task<ExchangeRateResult> FetchExchangeRateFromCoinMarketCapAsync(
        CryptoCurrencyType currency, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.CoinMarketCapApiKey))
        {
            throw new InvalidOperationException("CoinMarketCap API key not configured");
        }

        var symbolMap = new Dictionary<CryptoCurrencyType, string>
        {
            [CryptoCurrencyType.Bitcoin] = "BTC",
            [CryptoCurrencyType.Ethereum] = "ETH",
            [CryptoCurrencyType.USDT] = "USDT",
            [CryptoCurrencyType.USDC] = "USDC"
        };

        if (!symbolMap.TryGetValue(currency, out var symbol))
        {
            throw new ArgumentException($"Unsupported currency: {currency}");
        }

        var url = $"{_options.BackupApiUrl}/cryptocurrency/quotes/latest?symbol={symbol}&convert=USD";
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-CMC_PRO_API_KEY", _options.CoinMarketCapApiKey);
        request.Headers.Add("Accept", "application/json");

        var response = await _retryPipeline.ExecuteAsync(async (ct) =>
        {
            return await _httpClient.SendAsync(request, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<JsonElement>(content);

        var coinData = data.GetProperty("data").GetProperty(symbol);
        var quote = coinData.GetProperty("quote").GetProperty("USD");
        
        var price = quote.GetProperty("price").GetDecimal();
        var priceChange = quote.TryGetProperty("percent_change_24h", out var changeElement) 
            ? changeElement.GetDecimal() 
            : 0m;

        return new ExchangeRateResult(
            currency,
            price,
            priceChange,
            DateTime.UtcNow,
            "CoinMarketCap"
        );
    }
}