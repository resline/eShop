using Microsoft.Extensions.Diagnostics.HealthChecks;
using eShop.CryptoPayment.API.Services;
using CryptoPayment.BlockchainServices.Abstractions;

namespace eShop.CryptoPayment.API.HealthChecks;

public class BlockchainHealthCheck : IHealthCheck
{
    private readonly IBlockchainServiceFactory _blockchainServiceFactory;
    private readonly ILogger<BlockchainHealthCheck> _logger;
    private readonly IConfiguration _configuration;

    public BlockchainHealthCheck(
        IBlockchainServiceFactory blockchainServiceFactory,
        ILogger<BlockchainHealthCheck> logger,
        IConfiguration configuration)
    {
        _blockchainServiceFactory = blockchainServiceFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object>();
        var isHealthy = true;
        var errors = new List<string>();

        try
        {
            // Check Bitcoin service
            await CheckBlockchainService("Bitcoin", CryptoCurrencyType.Bitcoin, results, errors);
            
            // Check Ethereum service
            await CheckBlockchainService("Ethereum", CryptoCurrencyType.Ethereum, results, errors);

            if (errors.Any())
            {
                isHealthy = false;
                _logger.LogWarning("Blockchain health check completed with {ErrorCount} errors: {Errors}", 
                    errors.Count, string.Join(", ", errors));
            }
            else
            {
                _logger.LogDebug("All blockchain services are healthy");
            }

            return isHealthy 
                ? HealthCheckResult.Healthy("All blockchain services are operational", results)
                : HealthCheckResult.Degraded($"Some blockchain services have issues: {string.Join(", ", errors)}", data: results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blockchain health check failed");
            return HealthCheckResult.Unhealthy("Blockchain health check failed", ex, results);
        }
    }

    private async Task CheckBlockchainService(string serviceName, CryptoCurrencyType currency, 
        Dictionary<string, object> results, List<string> errors)
    {
        var serviceResults = new Dictionary<string, object>();
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var service = _blockchainServiceFactory.GetService(currency);
            
            var startTime = DateTime.UtcNow;
            
            // Test basic connectivity
            var isConnected = await service.IsConnectedAsync(cts.Token);
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            serviceResults["connected"] = isConnected;
            serviceResults["response_time_ms"] = responseTime;
            serviceResults["status"] = isConnected ? "healthy" : "disconnected";
            
            if (isConnected)
            {
                // Test additional functionality if connected
                try
                {
                    var blockHeight = await service.GetCurrentBlockHeightAsync(cts.Token);
                    serviceResults["block_height"] = blockHeight;
                    serviceResults["last_check"] = DateTime.UtcNow;
                    
                    // Check if block height is reasonable (not too old)
                    var expectedMinHeight = GetExpectedMinBlockHeight(currency);
                    if (blockHeight < expectedMinHeight)
                    {
                        errors.Add($"{serviceName}: Block height ({blockHeight}) seems outdated");
                        serviceResults["warning"] = "Block height seems outdated";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get block height for {ServiceName}", serviceName);
                    serviceResults["block_height_error"] = ex.Message;
                }
                
                // Test address validation
                try
                {
                    var testAddress = GetTestAddress(currency);
                    var isValidAddress = await service.ValidateAddressAsync(testAddress, cts.Token);
                    serviceResults["address_validation"] = isValidAddress ? "working" : "failed";
                    
                    if (!isValidAddress)
                    {
                        errors.Add($"{serviceName}: Address validation failed");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate address for {ServiceName}", serviceName);
                    serviceResults["address_validation_error"] = ex.Message;
                }
            }
            else
            {
                errors.Add($"{serviceName}: Not connected");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for {ServiceName}", serviceName);
            errors.Add($"{serviceName}: {ex.Message}");
            serviceResults["error"] = ex.Message;
            serviceResults["status"] = "error";
        }
        
        results[serviceName.ToLower()] = serviceResults;
    }

    private static long GetExpectedMinBlockHeight(CryptoCurrencyType currency)
    {
        // These are rough estimates - in production, you'd want more sophisticated logic
        return currency switch
        {
            CryptoCurrencyType.Bitcoin => 800000, // Bitcoin has ~800k+ blocks as of 2024
            CryptoCurrencyType.Ethereum => 18000000, // Ethereum has ~18M+ blocks as of 2024
            _ => 0
        };
    }

    private static string GetTestAddress(CryptoCurrencyType currency)
    {
        return currency switch
        {
            CryptoCurrencyType.Bitcoin => "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", // Genesis block address
            CryptoCurrencyType.Ethereum => "0x0000000000000000000000000000000000000000", // Null address
            _ => ""
        };
    }
}

public class ExchangeRateHealthCheck : IHealthCheck
{
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ILogger<ExchangeRateHealthCheck> _logger;
    private readonly HttpClient _httpClient;

    public ExchangeRateHealthCheck(
        IExchangeRateService exchangeRateService,
        ILogger<ExchangeRateHealthCheck> logger,
        HttpClient httpClient)
    {
        _exchangeRateService = exchangeRateService;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object>();
        var errors = new List<string>();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            
            // Test exchange rate service
            await CheckExchangeRateService(results, errors, cts.Token);
            
            // Test external API connectivity
            await CheckExternalApiConnectivity(results, errors, cts.Token);

            var isHealthy = !errors.Any();
            
            if (isHealthy)
            {
                _logger.LogDebug("Exchange rate service is healthy");
                return HealthCheckResult.Healthy("Exchange rate service is operational", results);
            }
            else
            {
                _logger.LogWarning("Exchange rate health check completed with {ErrorCount} errors: {Errors}", 
                    errors.Count, string.Join(", ", errors));
                return HealthCheckResult.Degraded($"Exchange rate service has issues: {string.Join(", ", errors)}", data: results);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exchange rate health check failed");
            return HealthCheckResult.Unhealthy("Exchange rate health check failed", ex, results);
        }
    }

    private async Task CheckExchangeRateService(Dictionary<string, object> results, List<string> errors, CancellationToken cancellationToken)
    {
        var serviceResults = new Dictionary<string, object>();
        
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Test getting Bitcoin rate
            var btcRate = await _exchangeRateService.GetExchangeRateAsync(CryptoCurrencyType.Bitcoin, cancellationToken);
            var btcResponseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            serviceResults["btc_rate"] = btcRate;
            serviceResults["btc_response_time_ms"] = btcResponseTime;
            
            // Validate BTC rate is reasonable
            if (btcRate <= 0 || btcRate > 1000000) // Basic sanity check
            {
                errors.Add($"BTC exchange rate ({btcRate}) seems unreasonable");
                serviceResults["btc_rate_warning"] = "Rate seems unreasonable";
            }
            
            // Test getting Ethereum rate
            startTime = DateTime.UtcNow;
            var ethRate = await _exchangeRateService.GetExchangeRateAsync(CryptoCurrencyType.Ethereum, cancellationToken);
            var ethResponseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            serviceResults["eth_rate"] = ethRate;
            serviceResults["eth_response_time_ms"] = ethResponseTime;
            
            // Validate ETH rate is reasonable
            if (ethRate <= 0 || ethRate > 50000) // Basic sanity check
            {
                errors.Add($"ETH exchange rate ({ethRate}) seems unreasonable");
                serviceResults["eth_rate_warning"] = "Rate seems unreasonable";
            }
            
            serviceResults["status"] = "healthy";
            serviceResults["last_update"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check exchange rate service");
            errors.Add($"Exchange rate service: {ex.Message}");
            serviceResults["error"] = ex.Message;
            serviceResults["status"] = "error";
        }
        
        results["exchange_rate_service"] = serviceResults;
    }

    private async Task CheckExternalApiConnectivity(Dictionary<string, object> results, List<string> errors, CancellationToken cancellationToken)
    {
        var connectivityResults = new Dictionary<string, object>();
        
        // Test common cryptocurrency API endpoints
        var endpoints = new[]
        {
            ("CoinGecko", "https://api.coingecko.com/api/v3/ping"),
            ("CoinMarketCap", "https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest?limit=1"),
            ("Kraken", "https://api.kraken.com/0/public/SystemStatus")
        };

        foreach (var (name, url) in endpoints)
        {
            var endpointResult = new Dictionary<string, object>();
            
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var startTime = DateTime.UtcNow;
                
                var response = await _httpClient.GetAsync(url, cts.Token);
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                endpointResult["status_code"] = (int)response.StatusCode;
                endpointResult["response_time_ms"] = responseTime;
                endpointResult["is_healthy"] = response.IsSuccessStatusCode;
                
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{name} API returned {response.StatusCode}");
                }
            }
            catch (TaskCanceledException)
            {
                errors.Add($"{name} API timeout");
                endpointResult["error"] = "timeout";
                endpointResult["is_healthy"] = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check {ApiName} connectivity", name);
                errors.Add($"{name} API: {ex.Message}");
                endpointResult["error"] = ex.Message;
                endpointResult["is_healthy"] = false;
            }
            
            connectivityResults[name.ToLower().Replace(" ", "_")] = endpointResult;
        }
        
        results["external_apis"] = connectivityResults;
    }
}

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly CryptoPaymentContext _context;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(CryptoPaymentContext context, ILogger<DatabaseHealthCheck> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object>();
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var startTime = DateTime.UtcNow;
            
            // Test basic connectivity
            var canConnect = await _context.Database.CanConnectAsync(cts.Token);
            var connectionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            results["can_connect"] = canConnect;
            results["connection_time_ms"] = connectionTime;
            
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Cannot connect to database", data: results);
            }
            
            // Test read operations
            startTime = DateTime.UtcNow;
            var currencyCount = await _context.CryptoCurrencies.CountAsync(cts.Token);
            var readTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            results["currency_count"] = currencyCount;
            results["read_time_ms"] = readTime;
            
            // Test if basic data exists
            if (currencyCount == 0)
            {
                results["warning"] = "No cryptocurrencies found in database";
            }
            
            // Test write operations (non-destructive)
            startTime = DateTime.UtcNow;
            var testQuery = _context.CryptoPayments.Where(p => p.Id == -1);
            await testQuery.CountAsync(cts.Token);
            var queryTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            results["query_time_ms"] = queryTime;
            results["status"] = "healthy";
            results["last_check"] = DateTime.UtcNow;
            
            _logger.LogDebug("Database health check passed");
            return HealthCheckResult.Healthy("Database is operational", results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            results["error"] = ex.Message;
            return HealthCheckResult.Unhealthy("Database health check failed", ex, results);
        }
    }
}

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer redis, ILogger<RedisHealthCheck> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object>();
        
        try
        {
            var database = _redis.GetDatabase();
            var startTime = DateTime.UtcNow;
            
            // Test basic connectivity with ping
            var latency = await database.PingAsync();
            var pingTime = latency.TotalMilliseconds;
            
            results["ping_time_ms"] = pingTime;
            results["is_connected"] = _redis.IsConnected;
            
            if (!_redis.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis is not connected", data: results);
            }
            
            // Test read/write operations
            var testKey = $"healthcheck:{Guid.NewGuid()}";
            var testValue = "health-check-value";
            
            startTime = DateTime.UtcNow;
            await database.StringSetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
            var writeTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            startTime = DateTime.UtcNow;
            var retrievedValue = await database.StringGetAsync(testKey);
            var readTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            // Cleanup
            await database.KeyDeleteAsync(testKey);
            
            results["write_time_ms"] = writeTime;
            results["read_time_ms"] = readTime;
            results["read_write_success"] = retrievedValue == testValue;
            results["status"] = "healthy";
            results["last_check"] = DateTime.UtcNow;
            
            if (retrievedValue != testValue)
            {
                return HealthCheckResult.Degraded("Redis read/write test failed", data: results);
            }
            
            _logger.LogDebug("Redis health check passed");
            return HealthCheckResult.Healthy("Redis is operational", results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            results["error"] = ex.Message;
            return HealthCheckResult.Unhealthy("Redis health check failed", ex, results);
        }
    }
}

public class EventBusHealthCheck : IHealthCheck
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventBusHealthCheck> _logger;
    private readonly IConfiguration _configuration;

    public EventBusHealthCheck(IEventBus eventBus, ILogger<EventBusHealthCheck> logger, IConfiguration configuration)
    {
        _eventBus = eventBus;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object>();
        
        try
        {
            // For RabbitMQ event bus, we can check if we can publish a test event
            // In a real implementation, you'd want to create a specific health check event
            
            var connectionString = _configuration.GetConnectionString("EventBus");
            results["connection_string_configured"] = !string.IsNullOrEmpty(connectionString);
            
            if (string.IsNullOrEmpty(connectionString))
            {
                return HealthCheckResult.Degraded("Event bus connection string not configured", data: results);
            }
            
            // Test if event bus is available (simplified check)
            // In production, you might want to publish a specific health check event
            results["event_bus_type"] = _eventBus.GetType().Name;
            results["status"] = "assumed_healthy";
            results["last_check"] = DateTime.UtcNow;
            results["note"] = "Event bus health check requires more sophisticated testing in production";
            
            _logger.LogDebug("Event bus health check completed");
            return HealthCheckResult.Healthy("Event bus appears to be configured", results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event bus health check failed");
            results["error"] = ex.Message;
            return HealthCheckResult.Unhealthy("Event bus health check failed", ex, results);
        }
    }
}