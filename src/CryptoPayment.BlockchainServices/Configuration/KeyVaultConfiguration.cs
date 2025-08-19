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
            _logger.LogDebug("Retrieved key {KeyName} from environment variable", keyName);
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
                    _logger.LogDebug("Retrieved key {KeyName} from environment variable {EnvVar}", keyName, envVarName);
                    return envVar;
                }
                else
                {
                    _logger.LogWarning("Environment variable {EnvVar} not found for key {KeyName}", envVarName, keyName);
                    throw new InvalidOperationException($"Required environment variable {envVarName} not found");
                }
            }
            return configValue;
        }

        _logger.LogError("Key {KeyName} not found in configuration or environment variables", keyName);
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
        var derivationInput = $"{masterKey}:{purpose}";
        
        if (!string.IsNullOrEmpty(salt))
        {
            derivationInput += $":{salt}";
        }

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(derivationInput));
        return Convert.ToBase64String(hash);
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