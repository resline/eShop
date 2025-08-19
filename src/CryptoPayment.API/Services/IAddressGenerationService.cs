namespace eShop.CryptoPayment.API.Services;

public interface IAddressGenerationService
{
    Task<PaymentAddress> GenerateAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task<PaymentAddress?> GetUnusedAddressAsync(CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
    Task MarkAddressAsUsedAsync(int addressId, CancellationToken cancellationToken = default);
    Task<bool> ValidateAddressAsync(string address, CryptoCurrencyType cryptoCurrency, CancellationToken cancellationToken = default);
}