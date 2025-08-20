using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Polly;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;
using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _httpClient;
    private readonly IDistributedCache _cache;
    private readonly ExchangeRateOptions _options;
    private readonly ILogger<ExchangeRateService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;
    private readonly ResiliencePipeline<HttpResponseMessage> _circuitBreakerPipeline;
    
    // Secondary cache for emergency fallback
    private readonly Dictionary<CryptoCurrencyType, ExchangeRateResult> _emergencyCache;
    private readonly object _emergencyCacheLock = new();

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
        _emergencyCache = new Dictionary<CryptoCurrencyType, ExchangeRateResult>();
        
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
        
        // Configure circuit breaker (5 failures opens circuit for 1 minute)
        _circuitBreakerPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new()
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                FailureRatio = 0.5, // Open circuit if 50% of recent calls fail
                SamplingDuration = TimeSpan.FromSeconds(30), // Sample over 30 seconds
                MinimumThroughput = 5, // Need at least 5 calls before considering circuit state
                BreakDuration = TimeSpan.FromMinutes(1), // Keep circuit open for 1 minute
                OnOpened = args =>
                {
                    _logger.LogWarning("Circuit breaker opened for exchange rate service due to {FailureCount} failures",
                        args.FailureCount);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker closed for exchange rate service");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("Circuit breaker half-opened for exchange rate service");
                    return ValueTask.CompletedTask;
                }
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
        
        // Try to get from secondary cache if primary cache fails
        var secondaryCachedResult = GetFromEmergencyCache(currency);
        if (secondaryCachedResult != null && 
            DateTime.UtcNow - secondaryCachedResult.LastUpdated < _options.EmergencyCacheMaxAge)
        {
            _logger.LogInformation("Retrieved exchange rate for {Currency} from emergency cache (age: {Age})", 
                currency, DateTime.UtcNow - secondaryCachedResult.LastUpdated);
            return secondaryCachedResult;
        }

        // Fetch from API
        try
        {
            var result = await FetchExchangeRateFromCoinGeckoAsync(currency, cancellationToken);
            
            // Cache the result in both primary and emergency cache
            await CacheResultAsync(cacheKey, result, cancellationToken);
            UpdateEmergencyCache(currency, result);
            
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
                UpdateEmergencyCache(currency, result);
                return result;
            }
            catch (Exception backupEx)
            {
                _logger.LogError(backupEx, "All exchange rate APIs failed for {Currency}", currency);
                
                // Try emergency cache as last resort
                var emergencyResult = GetFromEmergencyCache(currency);
                if (emergencyResult != null)
                {
                    _logger.LogWarning("Serving stale data from emergency cache for {Currency} (age: {Age})", 
                        currency, DateTime.UtcNow - emergencyResult.LastUpdated);
                    return emergencyResult with { Source = $"{emergencyResult.Source} (stale)" };
                }
                
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
                AbsoluteExpirationRelativeToNow = _options.CacheExpiry,
                SlidingExpiration = TimeSpan.FromMinutes(1) // Keep frequently accessed data longer
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
        
        var response = await _circuitBreakerPipeline.ExecuteAsync(async (ct) =>
        {
            return await _retryPipeline.ExecuteAsync(async (innerCt) =>
            {
                return await _httpClient.GetAsync(url, innerCt);
            }, ct);
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

        var response = await _circuitBreakerPipeline.ExecuteAsync(async (ct) =>
        {
            return await _retryPipeline.ExecuteAsync(async (innerCt) =>
            {
                return await _httpClient.SendAsync(request, innerCt);
            }, ct);
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
    
    private ExchangeRateResult? GetFromEmergencyCache(CryptoCurrencyType currency)
    {
        lock (_emergencyCacheLock)
        {
            return _emergencyCache.TryGetValue(currency, out var result) ? result : null;
        }
    }
    
    private void UpdateEmergencyCache(CryptoCurrencyType currency, ExchangeRateResult result)
    {
        lock (_emergencyCacheLock)
        {
            _emergencyCache[currency] = result;
            
            // Keep emergency cache size reasonable (remove entries older than 1 hour)
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var keysToRemove = _emergencyCache
                .Where(kvp => kvp.Value.LastUpdated < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var key in keysToRemove)
            {
                _emergencyCache.Remove(key);
            }
        }
    }
    
    public CircuitBreakerState GetCircuitBreakerState()
    {
        // Note: Polly v8 doesn't expose circuit state directly
        // This would need to be implemented with manual state tracking
        // For now, we'll use a simplified approach
        return CircuitBreakerState.Closed; // Placeholder
    }
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}