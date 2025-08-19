namespace CryptoPayment.BlockchainServices.Abstractions;

public interface IKeyManager
{
    Task<string> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default);
    Task<string> GetPublicKeyAsync(string privateKey, CancellationToken cancellationToken = default);
    Task<string> SignTransactionAsync(string transactionData, string privateKey, CancellationToken cancellationToken = default);
    Task<bool> ValidatePrivateKeyAsync(string privateKey, string currency, CancellationToken cancellationToken = default);
    Task<string> EncryptPrivateKeyAsync(string privateKey, CancellationToken cancellationToken = default);
    Task<string> DecryptPrivateKeyAsync(string encryptedPrivateKey, CancellationToken cancellationToken = default);
}