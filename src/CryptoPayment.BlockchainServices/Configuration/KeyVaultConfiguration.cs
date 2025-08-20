using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoPayment.BlockchainServices.Configuration;

public class KeyVaultConfiguration
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeyVaultConfiguration> _logger;

    public KeyVaultConfiguration(IConfiguration configuration, ILogger<KeyVaultConfiguration> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string GetSecureKey(string keyName)
    {
        // First try environment variable
        var envValue = Environment.GetEnvironmentVariable(keyName);
        if (!string.IsNullOrEmpty(envValue))
        {
            _logger.LogInformation("Retrieved key from environment variable successfully");
            return envValue;
        }

        // Then try configuration with placeholder replacement
        var configValue = _configuration[keyName];
        if (!string.IsNullOrEmpty(configValue))
        {
            // Handle ${VAR_NAME} placeholder format
            if (configValue.StartsWith("${") && configValue.EndsWith("}"))
            {
                var envVarName = configValue[2..^1]; // Remove ${ and }
                var envVar = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrEmpty(envVar))
                {
                    _logger.LogInformation("Retrieved key from environment variable successfully");
                    return envVar;
                }
                else
                {
                    _logger.LogWarning("Required environment variable not found for configuration key");
                    throw new InvalidOperationException($"Required environment variable {envVarName} not found");
                }
            }
            return configValue;
        }

        _logger.LogError("Required configuration key not found in configuration or environment variables");
        throw new InvalidOperationException($"Required key {keyName} not found");
    }

    public bool TryGetSecureKey(string keyName, out string? value)
    {
        try
        {
            value = GetSecureKey(keyName);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    public string GetMasterKey()
    {
        var masterKey = GetSecureKey("CRYPTO_PAYMENT_MASTER_KEY");
        if (string.IsNullOrEmpty(masterKey))
        {
            throw new InvalidOperationException("Master encryption key is required but not configured");
        }
        return masterKey;
    }

    public string DeriveEncryptionKey(string purpose, string? salt = null)
    {
        var masterKey = GetMasterKey();
        
        // Convert master key to bytes (assume base64 encoded)
        byte[] masterKeyBytes;
        try
        {
            masterKeyBytes = Convert.FromBase64String(masterKey);
        }
        catch (FormatException)
        {
            // Fallback to UTF8 bytes if not base64
            masterKeyBytes = System.Text.Encoding.UTF8.GetBytes(masterKey);
        }

        // Use purpose as info parameter for HKDF
        var info = System.Text.Encoding.UTF8.GetBytes(purpose);
        
        // Use provided salt or generate a default one for backward compatibility
        byte[] saltBytes;
        if (!string.IsNullOrEmpty(salt))
        {
            saltBytes = System.Text.Encoding.UTF8.GetBytes(salt);
        }
        else
        {
            // Use a static salt derived from purpose for backward compatibility
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            saltBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"salt_{purpose}")).Take(16).ToArray();
        }

        // Use HKDF to derive a 256-bit (32 byte) key
        const int outputLength = 32;
        var derivedKey = System.Security.Cryptography.HKDF.Extract(System.Security.Cryptography.HashAlgorithmName.SHA256, masterKeyBytes, saltBytes);
        var expandedKey = System.Security.Cryptography.HKDF.Expand(System.Security.Cryptography.HashAlgorithmName.SHA256, derivedKey, outputLength, info);
        
        // Clear sensitive data from memory
        Array.Clear(masterKeyBytes, 0, masterKeyBytes.Length);
        Array.Clear(derivedKey, 0, derivedKey.Length);
        
        return Convert.ToBase64String(expandedKey);
    }
}

public static class KeyVaultExtensions
{
    public static IServiceCollection AddKeyVaultConfiguration(this IServiceCollection services)
    {
        services.AddSingleton<KeyVaultConfiguration>();
        return services;
    }

    public static string GetSecureConnectionString(this IConfiguration configuration, string name)
    {
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{name}' not found");
        }

        // Replace ${VAR_NAME} placeholders with environment variables
        var pattern = @"\$\{([^}]+)\}";
        return System.Text.RegularExpressions.Regex.Replace(connectionString, pattern, match =>
        {
            var envVarName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (string.IsNullOrEmpty(envValue))
            {
                throw new InvalidOperationException($"Environment variable '{envVarName}' required for connection string not found");
            }
            return envValue;
        });
    }

    public static T GetSecureConfiguration<T>(this IConfiguration configuration, string sectionName) where T : class, new()
    {
        var section = configuration.GetSection(sectionName);
        var config = new T();
        section.Bind(config);

        // Use reflection to find string properties and replace placeholders
        var stringProperties = typeof(T).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.CanWrite);

        foreach (var property in stringProperties)
        {
            var value = property.GetValue(config) as string;
            if (!string.IsNullOrEmpty(value) && value.StartsWith("${") && value.EndsWith("}"))
            {
                var envVarName = value[2..^1];
                var envValue = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrEmpty(envValue))
                {
                    property.SetValue(config, envValue);
                }
                else
                {
                    throw new InvalidOperationException($"Environment variable '{envVarName}' required for {sectionName}.{property.Name} not found");
                }
            }
        }

        return config;
    }
}