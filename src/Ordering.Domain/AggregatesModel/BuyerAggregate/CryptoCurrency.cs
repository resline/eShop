using eShop.Ordering.Domain.SeedWork;

namespace eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

public sealed class CryptoCurrency : ValueObject
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Symbol { get; init; }
    public required string NetworkName { get; init; }

    public static readonly CryptoCurrency Bitcoin = new() { Id = 1, Name = "Bitcoin", Symbol = "BTC", NetworkName = "Bitcoin" };
    public static readonly CryptoCurrency Ethereum = new() { Id = 2, Name = "Ethereum", Symbol = "ETH", NetworkName = "Ethereum" };
    public static readonly CryptoCurrency USDT = new() { Id = 3, Name = "Tether USD", Symbol = "USDT", NetworkName = "Ethereum" };
    public static readonly CryptoCurrency USDC = new() { Id = 4, Name = "USD Coin", Symbol = "USDC", NetworkName = "Ethereum" };

    public static IEnumerable<CryptoCurrency> SupportedCurrencies =>
        new[] { Bitcoin, Ethereum, USDT, USDC };

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Id;
        yield return Symbol;
        yield return NetworkName;
    }

    public static CryptoCurrency FromId(int id)
    {
        return SupportedCurrencies.SingleOrDefault(c => c.Id == id)
            ?? throw new ArgumentException($"Unsupported cryptocurrency with ID: {id}");
    }

    public static CryptoCurrency FromSymbol(string symbol)
    {
        return SupportedCurrencies.SingleOrDefault(c => c.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unsupported cryptocurrency with symbol: {symbol}");
    }
}