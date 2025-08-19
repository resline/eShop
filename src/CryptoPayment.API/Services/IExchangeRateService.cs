using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

public interface IExchangeRateService
{
    Task<ExchangeRateResult> GetExchangeRateAsync(CryptoCurrencyType currency, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<CryptoCurrencyType, ExchangeRateResult>> GetExchangeRatesAsync(IEnumerable<CryptoCurrencyType> currencies, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

public record ExchangeRateResult(
    CryptoCurrencyType Currency,
    decimal UsdPrice,
    decimal PriceChangePercentage24h,
    DateTime LastUpdated,
    string Source
);

public record ExchangeRateOptions
{
    public string PrimaryApiUrl { get; init; } = "https://api.coingecko.com/api/v3";
    public string BackupApiUrl { get; init; } = "https://pro-api.coinmarketcap.com/v1";
    public string? CoinMarketCapApiKey { get; init; }
    public TimeSpan CacheExpiry { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}