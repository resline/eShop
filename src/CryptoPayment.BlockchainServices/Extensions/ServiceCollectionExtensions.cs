using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Configuration;
using CryptoPayment.BlockchainServices.Monitoring;
using CryptoPayment.BlockchainServices.Providers;
using CryptoPayment.BlockchainServices.Security;
using CryptoPayment.BlockchainServices.Services;

namespace CryptoPayment.BlockchainServices.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlockchainServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options
        services.Configure<BlockchainOptions>(configuration.GetSection(BlockchainOptions.SectionName));
        
        // Validate configuration
        services.AddSingleton<IValidateOptions<BlockchainOptions>, BlockchainOptionsValidator>();

        // Register core services
        services.AddSingleton<IAddressValidator, AddressValidator>();
        services.AddSingleton<IKeyStorage, InMemoryKeyStorage>();
        services.AddSingleton<IKeyManager, KeyManager>();
        
        // Register blockchain services
        services.AddSingleton<IBitcoinService, BitcoinService>();
        services.AddSingleton<IEthereumService, EthereumService>();
        services.AddSingleton<IBlockchainServiceFactory, BlockchainServiceFactory>();
        
        // Register provider clients
        services.AddSingleton<ICoinbaseCommerceClient, CoinbaseCommerceClient>();
        services.AddSingleton<IBitPayClient, BitPayClient>();
        
        // Register monitoring service
        services.AddHostedService<TransactionMonitorService>();
        
        // Add HTTP clients for external APIs
        services.AddHttpClient();

        return services;
    }

    public static IServiceCollection AddBlockchainServices(
        this IServiceCollection services,
        Action<BlockchainOptions> configureOptions)
    {
        services.Configure(configureOptions);
        
        // Validate configuration
        services.AddSingleton<IValidateOptions<BlockchainOptions>, BlockchainOptionsValidator>();

        // Register core services
        services.AddSingleton<IAddressValidator, AddressValidator>();
        services.AddSingleton<IKeyStorage, InMemoryKeyStorage>();
        services.AddSingleton<IKeyManager, KeyManager>();
        
        // Register blockchain services
        services.AddSingleton<IBitcoinService, BitcoinService>();
        services.AddSingleton<IEthereumService, EthereumService>();
        services.AddSingleton<IBlockchainServiceFactory, BlockchainServiceFactory>();
        
        // Register provider clients
        services.AddSingleton<ICoinbaseCommerceClient, CoinbaseCommerceClient>();
        services.AddSingleton<IBitPayClient, BitPayClient>();
        
        // Register monitoring service
        services.AddHostedService<TransactionMonitorService>();
        
        // Add HTTP clients for external APIs
        services.AddHttpClient();

        return services;
    }
}

public class BlockchainOptionsValidator : IValidateOptions<BlockchainOptions>
{
    public ValidateOptionsResult Validate(string? name, BlockchainOptions options)
    {
        var errors = new List<string>();

        // Validate Bitcoin options
        if (string.IsNullOrWhiteSpace(options.Bitcoin.RpcEndpoint))
        {
            errors.Add("Bitcoin RPC endpoint is required");
        }

        if (options.Bitcoin.ConfirmationsRequired < 1)
        {
            errors.Add("Bitcoin confirmations required must be at least 1");
        }

        if (options.Bitcoin.MinimumAmount <= 0)
        {
            errors.Add("Bitcoin minimum amount must be greater than 0");
        }

        // Validate Ethereum options
        if (string.IsNullOrWhiteSpace(options.Ethereum.RpcEndpoint))
        {
            errors.Add("Ethereum RPC endpoint is required");
        }

        if (options.Ethereum.ConfirmationsRequired < 1)
        {
            errors.Add("Ethereum confirmations required must be at least 1");
        }

        if (options.Ethereum.MinimumAmount <= 0)
        {
            errors.Add("Ethereum minimum amount must be greater than 0");
        }

        if (options.Ethereum.GasLimit <= 0)
        {
            errors.Add("Ethereum gas limit must be greater than 0");
        }

        // Validate security options
        if (options.Security.KeyStorageType != KeyStorageType.InMemory && 
            string.IsNullOrWhiteSpace(options.Security.EncryptionKey))
        {
            errors.Add("Encryption key is required when using persistent key storage");
        }

        // Validate monitoring options
        if (options.Monitoring.PollingInterval <= TimeSpan.Zero)
        {
            errors.Add("Monitoring polling interval must be greater than zero");
        }

        if (options.Monitoring.MaxReconnectAttempts < 1)
        {
            errors.Add("Max reconnect attempts must be at least 1");
        }

        // Validate provider options
        if (options.Providers.CoinbaseCommerce.Enabled && 
            string.IsNullOrWhiteSpace(options.Providers.CoinbaseCommerce.ApiKey))
        {
            errors.Add("Coinbase Commerce API key is required when enabled");
        }

        if (options.Providers.BitPay.Enabled && 
            string.IsNullOrWhiteSpace(options.Providers.BitPay.ApiToken))
        {
            errors.Add("BitPay API token is required when enabled");
        }

        return errors.Any() 
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}