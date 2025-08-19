using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Collections.Concurrent;

namespace eShop.CryptoPayment.API.Services;

public interface IServiceDegradationHandler
{
    Task<bool> IsServiceAvailableAsync(string serviceName);
    Task<T> ExecuteWithFallbackAsync<T>(string serviceName, Func<Task<T>> primaryAction, Func<Task<T>> fallbackAction);
    Task<T> ExecuteWithFallbackAsync<T>(string serviceName, Func<Task<T>> primaryAction, T fallbackValue);
    void MarkServiceAsUnavailable(string serviceName, TimeSpan? unavailableDuration = null);
    void MarkServiceAsAvailable(string serviceName);
    DegradationStatus GetDegradationStatus();
}

public class ServiceDegradationHandler : IServiceDegradationHandler
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<ServiceDegradationHandler> _logger;
    private readonly ConcurrentDictionary<string, ServiceStatus> _serviceStatuses;
    private readonly Timer _statusUpdateTimer;

    public ServiceDegradationHandler(
        HealthCheckService healthCheckService,
        ILogger<ServiceDegradationHandler> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
        _serviceStatuses = new ConcurrentDictionary<string, ServiceStatus>();
        
        // Update service statuses every 30 seconds
        _statusUpdateTimer = new Timer(UpdateServiceStatuses, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    public async Task<bool> IsServiceAvailableAsync(string serviceName)
    {
        if (_serviceStatuses.TryGetValue(serviceName, out var status))
        {
            // Check if the service is manually marked as unavailable and if the timeout has expired
            if (status.IsManuallyUnavailable && status.UnavailableUntil.HasValue && 
                DateTime.UtcNow > status.UnavailableUntil.Value)
            {
                status.IsManuallyUnavailable = false;
                status.UnavailableUntil = null;
                _logger.LogInformation("Service {ServiceName} automatically marked as available after timeout", serviceName);
            }

            return status.IsAvailable && !status.IsManuallyUnavailable;
        }

        // If we don't have status for the service, assume it's available
        return true;
    }

    public async Task<T> ExecuteWithFallbackAsync<T>(string serviceName, Func<Task<T>> primaryAction, Func<Task<T>> fallbackAction)
    {
        var isAvailable = await IsServiceAvailableAsync(serviceName);
        
        if (!isAvailable)
        {
            _logger.LogWarning("Service {ServiceName} is unavailable, using fallback", serviceName);
            return await fallbackAction();
        }

        try
        {
            var result = await primaryAction();
            MarkServiceAsAvailable(serviceName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Primary action failed for service {ServiceName}, using fallback", serviceName);
            MarkServiceAsUnavailable(serviceName, TimeSpan.FromMinutes(5)); // Mark as unavailable for 5 minutes
            return await fallbackAction();
        }
    }

    public async Task<T> ExecuteWithFallbackAsync<T>(string serviceName, Func<Task<T>> primaryAction, T fallbackValue)
    {
        return await ExecuteWithFallbackAsync(serviceName, primaryAction, () => Task.FromResult(fallbackValue));
    }

    public void MarkServiceAsUnavailable(string serviceName, TimeSpan? unavailableDuration = null)
    {
        var unavailableUntil = unavailableDuration.HasValue ? DateTime.UtcNow.Add(unavailableDuration.Value) : (DateTime?)null;
        
        _serviceStatuses.AddOrUpdate(serviceName, 
            new ServiceStatus { IsManuallyUnavailable = true, UnavailableUntil = unavailableUntil },
            (key, existing) => 
            {
                existing.IsManuallyUnavailable = true;
                existing.UnavailableUntil = unavailableUntil;
                return existing;
            });

        _logger.LogWarning("Service {ServiceName} marked as unavailable until {UnavailableUntil}", 
            serviceName, unavailableUntil?.ToString() ?? "manually restored");
    }

    public void MarkServiceAsAvailable(string serviceName)
    {
        _serviceStatuses.AddOrUpdate(serviceName,
            new ServiceStatus { IsAvailable = true, IsManuallyUnavailable = false },
            (key, existing) =>
            {
                existing.IsAvailable = true;
                existing.IsManuallyUnavailable = false;
                existing.UnavailableUntil = null;
                existing.LastSuccessfulCheck = DateTime.UtcNow;
                return existing;
            });
    }

    public DegradationStatus GetDegradationStatus()
    {
        var allServices = _serviceStatuses.ToArray();
        var availableServices = allServices.Count(s => s.Value.IsAvailable && !s.Value.IsManuallyUnavailable);
        var totalServices = allServices.Length;
        
        var degradedServices = allServices
            .Where(s => !s.Value.IsAvailable || s.Value.IsManuallyUnavailable)
            .Select(s => new DegradedService
            {
                Name = s.Key,
                Status = s.Value.HealthStatus?.ToString() ?? "Unknown",
                IsManuallyUnavailable = s.Value.IsManuallyUnavailable,
                UnavailableUntil = s.Value.UnavailableUntil,
                LastSuccessfulCheck = s.Value.LastSuccessfulCheck
            })
            .ToArray();

        return new DegradationStatus
        {
            OverallHealth = totalServices == 0 ? "Healthy" : 
                           availableServices == totalServices ? "Healthy" :
                           availableServices == 0 ? "Unhealthy" : "Degraded",
            AvailableServices = availableServices,
            TotalServices = totalServices,
            DegradedServices = degradedServices,
            LastUpdated = DateTime.UtcNow
        };
    }

    private async void UpdateServiceStatuses(object? state)
    {
        try
        {
            var healthReport = await _healthCheckService.CheckHealthAsync();
            
            foreach (var entry in healthReport.Entries)
            {
                var isAvailable = entry.Value.Status == HealthStatus.Healthy;
                
                _serviceStatuses.AddOrUpdate(entry.Key,
                    new ServiceStatus 
                    { 
                        IsAvailable = isAvailable, 
                        HealthStatus = entry.Value.Status,
                        LastHealthCheck = DateTime.UtcNow,
                        LastSuccessfulCheck = isAvailable ? DateTime.UtcNow : (DateTime?)null
                    },
                    (key, existing) =>
                    {
                        if (!existing.IsManuallyUnavailable)
                        {
                            existing.IsAvailable = isAvailable;
                        }
                        existing.HealthStatus = entry.Value.Status;
                        existing.LastHealthCheck = DateTime.UtcNow;
                        
                        if (isAvailable)
                        {
                            existing.LastSuccessfulCheck = DateTime.UtcNow;
                        }
                        
                        return existing;
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service statuses from health checks");
        }
    }

    public void Dispose()
    {
        _statusUpdateTimer?.Dispose();
    }
}

public class ServiceStatus
{
    public bool IsAvailable { get; set; } = true;
    public bool IsManuallyUnavailable { get; set; } = false;
    public DateTime? UnavailableUntil { get; set; }
    public HealthStatus? HealthStatus { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public DateTime? LastSuccessfulCheck { get; set; }
}

public class DegradationStatus
{
    public string OverallHealth { get; set; } = "Unknown";
    public int AvailableServices { get; set; }
    public int TotalServices { get; set; }
    public DegradedService[] DegradedServices { get; set; } = Array.Empty<DegradedService>();
    public DateTime LastUpdated { get; set; }
}

public class DegradedService
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsManuallyUnavailable { get; set; }
    public DateTime? UnavailableUntil { get; set; }
    public DateTime? LastSuccessfulCheck { get; set; }
}

// Enhanced Exchange Rate Service with Fallback
public class ResilientExchangeRateService : IExchangeRateService
{
    private readonly IExchangeRateService _primaryService;
    private readonly IServiceDegradationHandler _degradationHandler;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ResilientExchangeRateService> _logger;
    
    // Fallback rates (updated manually or from a secondary source)
    private readonly Dictionary<CryptoCurrencyType, decimal> _fallbackRates = new()
    {
        { CryptoCurrencyType.Bitcoin, 45000m },   // Approximate BTC rate
        { CryptoCurrencyType.Ethereum, 3000m }   // Approximate ETH rate
    };

    public ResilientExchangeRateService(
        IExchangeRateService primaryService,
        IServiceDegradationHandler degradationHandler,
        IMemoryCache cache,
        ILogger<ResilientExchangeRateService> logger)
    {
        _primaryService = primaryService;
        _degradationHandler = degradationHandler;
        _cache = cache;
        _logger = logger;
    }

    public async Task<decimal> GetExchangeRateAsync(CryptoCurrencyType currency, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"exchange_rate_{currency}";
        
        return await _degradationHandler.ExecuteWithFallbackAsync(
            "exchange_rate_service",
            async () =>
            {
                var rate = await _primaryService.GetExchangeRateAsync(currency, cancellationToken);
                
                // Cache successful results
                _cache.Set(cacheKey, rate, TimeSpan.FromMinutes(5));
                
                return rate;
            },
            async () =>
            {
                _logger.LogWarning("Using fallback exchange rate for {Currency}", currency);
                
                // Try to use cached value first
                if (_cache.TryGetValue(cacheKey, out decimal cachedRate))
                {
                    _logger.LogInformation("Using cached exchange rate for {Currency}: {Rate}", currency, cachedRate);
                    return cachedRate;
                }
                
                // Use fallback rate
                if (_fallbackRates.TryGetValue(currency, out var fallbackRate))
                {
                    _logger.LogWarning("Using hardcoded fallback exchange rate for {Currency}: {Rate}", currency, fallbackRate);
                    return fallbackRate;
                }
                
                // Last resort - return a very conservative rate
                _logger.LogError("No fallback rate available for {Currency}, using emergency rate", currency);
                return currency == CryptoCurrencyType.Bitcoin ? 30000m : 2000m;
            });
    }
}