using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.API.Services;

public interface IWebhookValidationService
{
    Task<bool> ValidateSignatureAsync(string payload, string signature, string? providerId = null, CancellationToken cancellationToken = default);
    Task<bool> ValidateTimestampAsync(string timestampHeader, TimeSpan tolerance = default, CancellationToken cancellationToken = default);
    Task<string> GenerateSignatureAsync(string payload, string? providerId = null, CancellationToken cancellationToken = default);
    Task RotateWebhookSecretAsync(string providerId, string newSecret, CancellationToken cancellationToken = default);
    bool IsValidPayload(string payload);
}

public class WebhookValidationService : IWebhookValidationService
{
    private readonly ILogger<WebhookValidationService> _logger;
    private readonly KeyVaultConfiguration _keyVault;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, string> _cachedSecrets = new();
    private readonly SemaphoreSlim _secretsCacheLock = new(1, 1);

    // Default tolerance for timestamp validation (5 minutes)
    private static readonly TimeSpan DefaultTimestampTolerance = TimeSpan.FromMinutes(5);

    public WebhookValidationService(
        ILogger<WebhookValidationService> logger,
        KeyVaultConfiguration keyVault,
        IConfiguration configuration)
    {
        _logger = logger;
        _keyVault = keyVault;
        _configuration = configuration;
    }

    public async Task<bool> ValidateSignatureAsync(string payload, string signature, string? providerId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Empty payload or signature provided for validation");
                return false;
            }

            var secret = await GetWebhookSecretAsync(providerId, cancellationToken);
            if (string.IsNullOrEmpty(secret))
            {
                _logger.LogError("Webhook secret not found for provider {ProviderId}", providerId ?? "default");
                return false;
            }

            var expectedSignature = await GenerateSignatureInternalAsync(payload, secret, cancellationToken);
            
            // Support multiple signature formats
            var normalizedSignature = NormalizeSignature(signature);
            var normalizedExpected = NormalizeSignature(expectedSignature);

            var isValid = SecureStringEquals(normalizedSignature, normalizedExpected);
            
            if (!isValid)
            {
                _logger.LogWarning("Webhook signature validation failed for provider {ProviderId}", providerId ?? "default");
            }
            else
            {
                _logger.LogDebug("Webhook signature validated successfully for provider {ProviderId}", providerId ?? "default");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature for provider {ProviderId}", providerId ?? "default");
            return false;
        }
    }

    public async Task<bool> ValidateTimestampAsync(string timestampHeader, TimeSpan tolerance = default, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(timestampHeader))
            {
                _logger.LogWarning("Empty timestamp header provided for validation");
                return false;
            }

            if (tolerance == default)
            {
                tolerance = DefaultTimestampTolerance;
            }

            // Try parsing Unix timestamp (seconds)
            if (long.TryParse(timestampHeader, out var unixSeconds))
            {
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                var now = DateTimeOffset.UtcNow;
                var age = now - timestamp;

                var isValid = Math.Abs(age.TotalMilliseconds) <= tolerance.TotalMilliseconds;
                
                if (!isValid)
                {
                    _logger.LogWarning("Webhook timestamp validation failed. Age: {Age}, Tolerance: {Tolerance}", age, tolerance);
                }

                return isValid;
            }

            // Try parsing ISO 8601 format
            if (DateTimeOffset.TryParse(timestampHeader, out var parsedTimestamp))
            {
                var now = DateTimeOffset.UtcNow;
                var age = now - parsedTimestamp;

                var isValid = Math.Abs(age.TotalMilliseconds) <= tolerance.TotalMilliseconds;
                
                if (!isValid)
                {
                    _logger.LogWarning("Webhook timestamp validation failed. Age: {Age}, Tolerance: {Tolerance}", age, tolerance);
                }

                return isValid;
            }

            _logger.LogWarning("Unable to parse timestamp header: {Timestamp}", timestampHeader);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook timestamp");
            return false;
        }
    }

    public async Task<string> GenerateSignatureAsync(string payload, string? providerId = null, CancellationToken cancellationToken = default)
    {
        var secret = await GetWebhookSecretAsync(providerId, cancellationToken);
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException($"Webhook secret not found for provider {providerId ?? "default"}");
        }

        return await GenerateSignatureInternalAsync(payload, secret, cancellationToken);
    }

    public async Task RotateWebhookSecretAsync(string providerId, string newSecret, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(newSecret))
            {
                throw new ArgumentException("New secret cannot be empty", nameof(newSecret));
            }

            // Store the new secret securely
            // In production, this would update Key Vault or secure storage
            await _secretsCacheLock.WaitAsync(cancellationToken);
            try
            {
                var secretKey = GetSecretCacheKey(providerId);
                _cachedSecrets[secretKey] = newSecret;
                
                _logger.LogInformation("Webhook secret rotated for provider {ProviderId}", providerId);
            }
            finally
            {
                _secretsCacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating webhook secret for provider {ProviderId}", providerId);
            throw;
        }
    }

    public bool IsValidPayload(string payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            // Basic JSON validation
            JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating webhook payload format");
            return false;
        }
    }

    private async Task<string?> GetWebhookSecretAsync(string? providerId, CancellationToken cancellationToken)
    {
        var secretKey = GetSecretCacheKey(providerId);
        
        // Check cache first
        await _secretsCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSecrets.TryGetValue(secretKey, out var cachedSecret))
            {
                return cachedSecret;
            }
        }
        finally
        {
            _secretsCacheLock.Release();
        }

        // Load from configuration/Key Vault
        try
        {
            var configKey = providerId switch
            {
                "coinbase" => "COINBASE_COMMERCE_WEBHOOK_SECRET",
                "bitpay" => "BITPAY_WEBHOOK_SECRET",
                _ => "CRYPTO_PAYMENT_WEBHOOK_SECRET"
            };

            var secret = _keyVault.TryGetSecureKey(configKey, out var value) ? value : null;
            
            if (!string.IsNullOrEmpty(secret))
            {
                // Cache the secret
                await _secretsCacheLock.WaitAsync(cancellationToken);
                try
                {
                    _cachedSecrets[secretKey] = secret;
                }
                finally
                {
                    _secretsCacheLock.Release();
                }
            }

            return secret;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webhook secret for provider {ProviderId}", providerId ?? "default");
            return null;
        }
    }

    private async Task<string> GenerateSignatureInternalAsync(string payload, string secret, CancellationToken cancellationToken)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        
        return "sha256=" + Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string NormalizeSignature(string signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return string.Empty;
        }

        // Remove common prefixes and normalize case
        var normalized = signature.ToLowerInvariant();
        
        if (normalized.StartsWith("sha256="))
        {
            normalized = normalized[7..];
        }
        else if (normalized.StartsWith("sha1="))
        {
            normalized = normalized[5..];
        }

        return normalized;
    }

    private static bool SecureStringEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }

    private static string GetSecretCacheKey(string? providerId)
    {
        return $"webhook_secret_{providerId ?? "default"}";
    }
}

public static class WebhookValidationExtensions
{
    public static IServiceCollection AddWebhookValidation(this IServiceCollection services)
    {
        services.AddSingleton<IWebhookValidationService, WebhookValidationService>();
        return services;
    }
}