using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Models;

namespace CryptoPayment.BlockchainServices.Services;

public class BlockchainServiceFactory : IBlockchainServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BlockchainServiceFactory> _logger;
    private readonly Dictionary<string, Type> _serviceTypes;

    public BlockchainServiceFactory(IServiceProvider serviceProvider, ILogger<BlockchainServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        _serviceTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = typeof(IBitcoinService),
            ["ETH"] = typeof(IEthereumService),
            ["USDT"] = typeof(IEthereumService),
            ["USDC"] = typeof(IEthereumService)
        };
    }

    public IBlockchainService GetService(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency cannot be null or empty", nameof(currency));
        }

        if (!_serviceTypes.TryGetValue(currency.ToUpperInvariant(), out var serviceType))
        {
            throw new NotSupportedException($"Currency {currency} is not supported");
        }

        var service = _serviceProvider.GetService(serviceType) as IBlockchainService;
        if (service == null)
        {
            throw new InvalidOperationException($"Service for currency {currency} is not registered");
        }

        _logger.LogDebug("Retrieved blockchain service for currency {Currency}", currency);
        return service;
    }

    public IBlockchainService GetService(CryptoCurrency currency)
    {
        return GetService(currency.ToString());
    }

    public IBitcoinService GetBitcoinService()
    {
        var service = _serviceProvider.GetService<IBitcoinService>();
        if (service == null)
        {
            throw new InvalidOperationException("Bitcoin service is not registered");
        }

        return service;
    }

    public IEthereumService GetEthereumService()
    {
        var service = _serviceProvider.GetService<IEthereumService>();
        if (service == null)
        {
            throw new InvalidOperationException("Ethereum service is not registered");
        }

        return service;
    }

    public IEnumerable<IBlockchainService> GetAllServices()
    {
        var services = new List<IBlockchainService>();

        try
        {
            services.Add(GetBitcoinService());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bitcoin service not available");
        }

        try
        {
            services.Add(GetEthereumService());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ethereum service not available");
        }

        return services;
    }
}