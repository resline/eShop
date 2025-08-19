using System.Security.Cryptography;
using System.Text;
using NBitcoin;
using Nethereum.Web3.Accounts;
using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.BlockchainServices.Security;

public class KeyManager : IKeyManager
{
    private readonly ILogger<KeyManager> _logger;
    private readonly SecurityOptions _options;
    private readonly IKeyStorage _keyStorage;

    public KeyManager(
        ILogger<KeyManager> logger,
        IOptions<BlockchainOptions> options,
        IKeyStorage keyStorage)
    {
        _logger = logger;
        _options = options.Value.Security;
        _keyStorage = keyStorage;
    }

    public async Task<string> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate a cryptographically secure private key
            using var rng = RandomNumberGenerator.Create();
            var privateKeyBytes = new byte[32];
            rng.GetBytes(privateKeyBytes);
            
            var privateKey = Convert.ToHexString(privateKeyBytes).ToLowerInvariant();
            
            _logger.LogDebug("Generated new private key");
            return privateKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate private key");
            throw new InvalidOperationException("Failed to generate private key", ex);
        }
    }

    public async Task<string> GetPublicKeyAsync(string privateKey, CancellationToken cancellationToken = default)
    {
        try
        {
            // For Ethereum-style keys
            if (privateKey.Length == 64 || (privateKey.StartsWith("0x") && privateKey.Length == 66))
            {
                var account = new Account(privateKey);
                return account.PublicKey;
            }
            
            // For Bitcoin-style keys
            if (IsValidBitcoinPrivateKey(privateKey))
            {
                var key = Key.Parse(privateKey, Network.TestNet);
                return key.PubKey.ToString();
            }

            throw new ArgumentException("Invalid private key format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get public key from private key");
            throw new InvalidOperationException("Failed to get public key", ex);
        }
    }

    public async Task<string> SignTransactionAsync(string transactionData, string privateKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var dataBytes = Encoding.UTF8.GetBytes(transactionData);
            
            // For Ethereum transactions
            if (privateKey.Length == 64 || (privateKey.StartsWith("0x") && privateKey.Length == 66))
            {
                var account = new Account(privateKey);
                // This is a simplified implementation
                // In production, you'd use proper transaction signing with Nethereum
                using var ecdsa = ECDsa.Create();
                var keyBytes = Convert.FromHexString(privateKey.Replace("0x", ""));
                ecdsa.ImportECPrivateKey(keyBytes, out _);
                
                var signature = ecdsa.SignData(dataBytes, HashAlgorithmName.SHA256);
                return Convert.ToBase64String(signature);
            }
            
            // For Bitcoin transactions
            if (IsValidBitcoinPrivateKey(privateKey))
            {
                var key = Key.Parse(privateKey, Network.TestNet);
                var hash = NBitcoin.Crypto.Hashes.SHA256(dataBytes);
                var signature = key.Sign(new uint256(hash));
                return signature.ToString();
            }

            throw new ArgumentException("Invalid private key format for signing");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign transaction");
            throw new InvalidOperationException("Failed to sign transaction", ex);
        }
    }

    public async Task<bool> ValidatePrivateKeyAsync(string privateKey, string currency, CancellationToken cancellationToken = default)
    {
        try
        {
            return currency.ToUpperInvariant() switch
            {
                "BTC" => IsValidBitcoinPrivateKey(privateKey),
                "ETH" or "USDT" or "USDC" => IsValidEthereumPrivateKey(privateKey),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating private key for currency {Currency}", currency);
            return false;
        }
    }

    public async Task<string> EncryptPrivateKeyAsync(string privateKey, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.EncryptionKey))
            {
                _logger.LogWarning("No encryption key configured, storing private key in plain text");
                return privateKey;
            }

            var encryptedKey = await _keyStorage.EncryptAsync(privateKey, _options.EncryptionKey, cancellationToken);
            _logger.LogDebug("Private key encrypted successfully");
            
            return encryptedKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt private key");
            throw new InvalidOperationException("Failed to encrypt private key", ex);
        }
    }

    public async Task<string> DecryptPrivateKeyAsync(string encryptedPrivateKey, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.EncryptionKey))
            {
                _logger.LogWarning("No encryption key configured, returning private key as is");
                return encryptedPrivateKey;
            }

            var decryptedKey = await _keyStorage.DecryptAsync(encryptedPrivateKey, _options.EncryptionKey, cancellationToken);
            _logger.LogDebug("Private key decrypted successfully");
            
            return decryptedKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt private key");
            throw new InvalidOperationException("Failed to decrypt private key", ex);
        }
    }

    private static bool IsValidBitcoinPrivateKey(string privateKey)
    {
        try
        {
            if (privateKey.Length == 64)
            {
                // Raw hex format
                var keyBytes = Convert.FromHexString(privateKey);
                return keyBytes.Length == 32;
            }
            
            // WIF format
            Key.Parse(privateKey, Network.TestNet);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidEthereumPrivateKey(string privateKey)
    {
        try
        {
            if (privateKey.StartsWith("0x"))
                privateKey = privateKey[2..];
                
            if (privateKey.Length != 64)
                return false;
                
            var keyBytes = Convert.FromHexString(privateKey);
            return keyBytes.Length == 32;
        }
        catch
        {
            return false;
        }
    }
}

public interface IKeyStorage
{
    Task<string> EncryptAsync(string data, string key, CancellationToken cancellationToken = default);
    Task<string> DecryptAsync(string encryptedData, string key, CancellationToken cancellationToken = default);
    Task<bool> StoreKeyAsync(string keyId, string encryptedKey, CancellationToken cancellationToken = default);
    Task<string?> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken = default);
    Task<bool> DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default);
}

public class InMemoryKeyStorage : IKeyStorage
{
    private readonly ILogger<InMemoryKeyStorage> _logger;
    private readonly ConcurrentDictionary<string, string> _keyStore;

    public InMemoryKeyStorage(ILogger<InMemoryKeyStorage> logger)
    {
        _logger = logger;
        _keyStore = new ConcurrentDictionary<string, string>();
    }

    public Task<string> EncryptAsync(string data, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKeyFromString(key);
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var encryptedBytes = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
            
            // Prepend IV to encrypted data
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
            
            return Task.FromResult(Convert.ToBase64String(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt data");
            throw new InvalidOperationException("Failed to encrypt data", ex);
        }
    }

    public Task<string> DecryptAsync(string encryptedData, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedData);
            
            using var aes = Aes.Create();
            aes.Key = DeriveKeyFromString(key);
            
            // Extract IV from encrypted data
            var iv = new byte[aes.IV.Length];
            var cipherBytes = new byte[encryptedBytes.Length - iv.Length];
            
            Buffer.BlockCopy(encryptedBytes, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(encryptedBytes, iv.Length, cipherBytes, 0, cipherBytes.Length);
            
            aes.IV = iv;
            
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            
            return Task.FromResult(Encoding.UTF8.GetString(decryptedBytes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt data");
            throw new InvalidOperationException("Failed to decrypt data", ex);
        }
    }

    public Task<bool> StoreKeyAsync(string keyId, string encryptedKey, CancellationToken cancellationToken = default)
    {
        _keyStore.TryAdd(keyId, encryptedKey);
        _logger.LogDebug("Stored key {KeyId}", keyId);
        return Task.FromResult(true);
    }

    public Task<string?> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        _keyStore.TryGetValue(keyId, out var key);
        return Task.FromResult(key);
    }

    public Task<bool> DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var removed = _keyStore.TryRemove(keyId, out _);
        if (removed)
        {
            _logger.LogDebug("Deleted key {KeyId}", keyId);
        }
        return Task.FromResult(removed);
    }

    private static byte[] DeriveKeyFromString(string keyString)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
    }
}