using Microsoft.Extensions.Logging;

namespace eShop.CryptoPayment.API.Infrastructure;

public class CryptoPaymentContextSeed : IDbSeeder<CryptoPaymentContext>
{
    public async Task SeedAsync(CryptoPaymentContext context)
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<CryptoPaymentContextSeed>();
        var policy = CreatePolicy(logger, nameof(CryptoPaymentContextSeed));

        await policy.ExecuteAsync(async () =>
        {
            if (!context.CryptoCurrencies.Any())
            {
                context.CryptoCurrencies.AddRange(GetPreconfiguredCryptoCurrencies());
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded cryptocurrency database");
            }
            
            if (!context.PaymentAddresses.Any())
            {
                context.PaymentAddresses.AddRange(await GetPreconfiguredAddressesAsync(context));
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded payment addresses database");
            }
        });
    }

    private static IEnumerable<CryptoCurrency> GetPreconfiguredCryptoCurrencies()
    {
        return new List<CryptoCurrency>
        {
            new CryptoCurrency
            {
                Id = 1,
                Symbol = "BTC",
                Name = "Bitcoin",
                Decimals = 8,
                NetworkName = "Bitcoin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new CryptoCurrency
            {
                Id = 2,
                Symbol = "ETH",
                Name = "Ethereum",
                Decimals = 18,
                NetworkName = "Ethereum",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
    }

    private static async Task<IEnumerable<PaymentAddress>> GetPreconfiguredAddressesAsync(CryptoPaymentContext context)
    {
        var btcCurrency = await context.CryptoCurrencies.FirstOrDefaultAsync(c => c.Symbol == "BTC");
        var ethCurrency = await context.CryptoCurrencies.FirstOrDefaultAsync(c => c.Symbol == "ETH");

        var addresses = new List<PaymentAddress>();

        if (btcCurrency != null)
        {
            // Pre-generate some Bitcoin addresses for testing
            addresses.AddRange(new[]
            {
                new PaymentAddress
                {
                    Address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
                    CryptoCurrencyId = btcCurrency.Id,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                },
                new PaymentAddress
                {
                    Address = "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    CryptoCurrencyId = btcCurrency.Id,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                }
            });
        }

        if (ethCurrency != null)
        {
            // Pre-generate some Ethereum addresses for testing
            addresses.AddRange(new[]
            {
                new PaymentAddress
                {
                    Address = "0xde0B295669a9FD93d5F28D9Ec85E40f4cb697BAe",
                    CryptoCurrencyId = ethCurrency.Id,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                },
                new PaymentAddress
                {
                    Address = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed",
                    CryptoCurrencyId = ethCurrency.Id,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                }
            });
        }

        return addresses;
    }

    private static AsyncRetryPolicy CreatePolicy(ILogger logger, string prefix, int retries = 3)
    {
        return Policy.Handle<SqlException>()
            .WaitAndRetryAsync(
                retryCount: retries,
                sleepDurationProvider: retry => TimeSpan.FromSeconds(5),
                onRetry: (outcome, timespan, retry, ctx) =>
                {
                    logger.LogWarning("Retrying {prefix} ({retry}/{retries}) in {delay}s", prefix, retry, retries, timespan.TotalSeconds);
                }
            );
    }
}