namespace eShop.CryptoPayment.API.Services;

public interface IAddressGenerationService
{
    Task<PaymentAddress> GenerateAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task<PaymentAddress?> GetUnusedAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task MarkAddressAsUsedAsync(int addressId, CancellationToken cancellationToken = default);
    Task<bool> ValidateAddressAsync(string address, CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task<int> GetAddressUsageCountAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task<int> GetUnusedAddressCountAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task<bool> PreGenerateAddressesAsync(CryptoCurrencyType cryptoCurrency, int count, CancellationToken cancellationToken = default);
}