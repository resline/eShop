using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Collections.Concurrent;
using Polly.CircuitBreaker;

namespace eShop.CryptoPayment.API.Services;

public interface IEnhancedServiceDegradationHandler : IServiceDegradationHandler
{
    Task<DegradationStatus> GetDetailedDegradationStatusAsync();
    Task<bool> TryRecoverServiceAsync(string serviceName);
    Task<T> ExecuteWithGradualDegradationAsync<T>(string serviceName, 
        Func<Task<T>> primaryAction, 
        Func<Task<T>>? degradedAction = null,
        Func<Task<T>>? fallbackAction = null);
    Task<ServiceRecoveryPlan> GetRecoveryPlanAsync(string serviceName);
}

public class EnhancedServiceDegradationHandler : IEnhancedServiceDegradationHandler
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<EnhancedServiceDegradationHandler> _logger;
    private readonly ConcurrentDictionary<string, EnhancedServiceStatus> _serviceStatuses;
    private readonly Timer _statusUpdateTimer;
    private readonly Timer _recoveryTimer;

    // Circuit breaker states for each service
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitStates;
    
    // Recovery strategies
    private readonly Dictionary<string, IServiceRecoveryStrategy> _recoveryStrategies;

    public EnhancedServiceDegradationHandler(
        HealthCheckService healthCheckService,
        ILogger<EnhancedServiceDegradationHandler> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
        _serviceStatuses = new ConcurrentDictionary<string, EnhancedServiceStatus>();
        _circuitStates = new ConcurrentDictionary<string, CircuitBreakerState>();
        _recoveryStrategies = new Dictionary<string, IServiceRecoveryStrategy>();
        
        // Update service statuses every 30 seconds
        _statusUpdateTimer = new Timer(UpdateServiceStatuses, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        
        // Attempt service recovery every 2 minutes
        _recoveryTimer = new Timer(AttemptServiceRecovery, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        
        // Initialize recovery strategies
        InitializeRecoveryStrategies();
    }

    public async Task<bool> IsServiceAvailableAsync(string serviceName)
    {
        if (_serviceStatuses.TryGetValue(serviceName, out var status))
        {
            // Check circuit breaker state
            var circuitState = _circuitStates.GetValueOrDefault(serviceName, CircuitBreakerState.Closed);
            if (circuitState == CircuitBreakerState.Open)
            {
                return false;
            }
            
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
        return await ExecuteWithGradualDegradationAsync(serviceName, primaryAction, null, fallbackAction);
    }

    public async Task<T> ExecuteWithFallbackAsync<T>(string serviceName, Func<Task<T>> primaryAction, T fallbackValue)
    {
        return await ExecuteWithFallbackAsync(serviceName, primaryAction, () => Task.FromResult(fallbackValue));
    }

    public async Task<T> ExecuteWithGradualDegradationAsync<T>(string serviceName, 
        Func<Task<T>> primaryAction, 
        Func<Task<T>>? degradedAction = null,
        Func<Task<T>>? fallbackAction = null)
    {
        var status = GetOrCreateServiceStatus(serviceName);
        var circuitState = _circuitStates.GetValueOrDefault(serviceName, CircuitBreakerState.Closed);
        
        // Determine which action to use based on service health
        if (circuitState == CircuitBreakerState.Open || status.DegradationLevel >= DegradationLevel.Critical)
        {
            if (fallbackAction != null)
            {
                _logger.LogWarning("Service {ServiceName} is critical/circuit open, using fallback", serviceName);
                return await ExecuteAction(fallbackAction, serviceName, "fallback");
            }
            throw new ServiceUnavailableException($"Service {serviceName} is unavailable and no fallback provided");
        }
        
        if (status.DegradationLevel >= DegradationLevel.Degraded && degradedAction != null)
        {
            _logger.LogWarning("Service {ServiceName} is degraded, using degraded action", serviceName);
            try
            {
                var result = await ExecuteAction(degradedAction, serviceName, "degraded");
                status.RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Degraded action failed for service {ServiceName}, falling back", serviceName);
                status.RecordFailure();
                
                if (fallbackAction != null)
                {
                    return await ExecuteAction(fallbackAction, serviceName, "fallback");
                }
                throw;
            }
        }

        // Try primary action
        if (await IsServiceAvailableAsync(serviceName))
        {
            try
            {
                var result = await ExecuteAction(primaryAction, serviceName, "primary");
                status.RecordSuccess();
                MarkServiceAsAvailable(serviceName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Primary action failed for service {ServiceName}", serviceName);
                status.RecordFailure();
                
                // Update circuit breaker state based on failure pattern
                UpdateCircuitBreakerState(serviceName, status);
                
                // Try degraded action if available
                if (degradedAction != null)
                {
                    try
                    {
                        _logger.LogInformation("Attempting degraded action for service {ServiceName}", serviceName);
                        return await ExecuteAction(degradedAction, serviceName, "degraded");
                    }
                    catch (Exception degradedEx)
                    {
                        _logger.LogError(degradedEx, "Degraded action also failed for service {ServiceName}", serviceName);
                    }
                }
                
                // Final fallback
                if (fallbackAction != null)
                {
                    _logger.LogWarning("Using fallback action for service {ServiceName}", serviceName);
                    return await ExecuteAction(fallbackAction, serviceName, "fallback");
                }
                
                throw;
            }
        }
        else
        {
            if (fallbackAction != null)
            {
                _logger.LogWarning("Service {ServiceName} is unavailable, using fallback", serviceName);
                return await ExecuteAction(fallbackAction, serviceName, "fallback");
            }
            
            throw new ServiceUnavailableException($"Service {serviceName} is unavailable");
        }
    }

    public void MarkServiceAsUnavailable(string serviceName, TimeSpan? unavailableDuration = null)
    {
        var unavailableUntil = unavailableDuration.HasValue ? DateTime.UtcNow.Add(unavailableDuration.Value) : (DateTime?)null;
        
        var status = GetOrCreateServiceStatus(serviceName);
        status.IsManuallyUnavailable = true;
        status.UnavailableUntil = unavailableUntil;
        status.DegradationLevel = DegradationLevel.Critical;

        _logger.LogWarning("Service {ServiceName} marked as unavailable until {UnavailableUntil}", 
            serviceName, unavailableUntil?.ToString() ?? "manually restored");
    }

    public void MarkServiceAsAvailable(string serviceName)
    {
        var status = GetOrCreateServiceStatus(serviceName);
        status.IsAvailable = true;
        status.IsManuallyUnavailable = false;
        status.UnavailableUntil = null;
        status.LastSuccessfulCheck = DateTime.UtcNow;
        status.DegradationLevel = DegradationLevel.Healthy;
        
        // Reset circuit breaker
        _circuitStates[serviceName] = CircuitBreakerState.Closed;
    }

    public DegradationStatus GetDegradationStatus()
    {
        var allServices = _serviceStatuses.ToArray();
        var availableServices = allServices.Count(s => s.Value.IsAvailable && !s.Value.IsManuallyUnavailable);
        var totalServices = allServices.Length;
        
        var degradedServices = allServices
            .Where(s => !s.Value.IsAvailable || s.Value.IsManuallyUnavailable || s.Value.DegradationLevel > DegradationLevel.Healthy)
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

    public async Task<DegradationStatus> GetDetailedDegradationStatusAsync()
    {
        var allServices = _serviceStatuses.ToArray();
        var detailedServices = new List<DetailedDegradedService>();
        
        foreach (var service in allServices)
        {
            var circuitState = _circuitStates.GetValueOrDefault(service.Key, CircuitBreakerState.Closed);
            var recoveryPlan = await GetRecoveryPlanAsync(service.Key);
            
            detailedServices.Add(new DetailedDegradedService
            {
                Name = service.Key,
                Status = service.Value.HealthStatus?.ToString() ?? "Unknown",
                DegradationLevel = service.Value.DegradationLevel,
                CircuitBreakerState = circuitState,
                IsManuallyUnavailable = service.Value.IsManuallyUnavailable,
                UnavailableUntil = service.Value.UnavailableUntil,
                LastSuccessfulCheck = service.Value.LastSuccessfulCheck,
                FailureCount = service.Value.ConsecutiveFailures,
                SuccessRate = service.Value.GetSuccessRate(),
                RecoveryPlan = recoveryPlan
            });
        }
        
        return new DegradationStatus
        {
            OverallHealth = CalculateOverallHealth(detailedServices),
            AvailableServices = detailedServices.Count(s => s.DegradationLevel == DegradationLevel.Healthy),
            TotalServices = detailedServices.Count,
            DegradedServices = detailedServices.Cast<DegradedService>().ToArray(),
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<bool> TryRecoverServiceAsync(string serviceName)
    {
        if (!_recoveryStrategies.TryGetValue(serviceName, out var strategy))
        {
            strategy = new DefaultRecoveryStrategy();
        }
        
        try
        {
            _logger.LogInformation("Attempting to recover service {ServiceName}", serviceName);
            var recovered = await strategy.TryRecoverAsync(serviceName);
            
            if (recovered)
            {
                MarkServiceAsAvailable(serviceName);
                _logger.LogInformation("Successfully recovered service {ServiceName}", serviceName);
            }
            else
            {
                _logger.LogWarning("Failed to recover service {ServiceName}", serviceName);
            }
            
            return recovered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during recovery attempt for service {ServiceName}", serviceName);
            return false;
        }
    }

    public async Task<ServiceRecoveryPlan> GetRecoveryPlanAsync(string serviceName)
    {
        var status = _serviceStatuses.GetValueOrDefault(serviceName);
        if (status == null)
        {
            return new ServiceRecoveryPlan { ServiceName = serviceName, IsHealthy = true };
        }
        
        var plan = new ServiceRecoveryPlan
        {
            ServiceName = serviceName,
            IsHealthy = status.DegradationLevel == DegradationLevel.Healthy,
            DegradationLevel = status.DegradationLevel,
            EstimatedRecoveryTime = CalculateEstimatedRecoveryTime(status),
            RecommendedActions = GenerateRecoveryActions(serviceName, status),
            CanAutoRecover = _recoveryStrategies.ContainsKey(serviceName),
            NextRecoveryAttempt = DateTime.UtcNow.AddMinutes(2) // Next automatic attempt
        };
        
        return plan;
    }

    private async Task<T> ExecuteAction<T>(Func<Task<T>> action, string serviceName, string actionType)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action();
            stopwatch.Stop();
            
            _logger.LogDebug("Service {ServiceName} {ActionType} action completed in {Duration}ms", 
                serviceName, actionType, stopwatch.ElapsedMilliseconds);
                
            return result;
        }
        catch
        {
            stopwatch.Stop();
            _logger.LogError("Service {ServiceName} {ActionType} action failed after {Duration}ms", 
                serviceName, actionType, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private EnhancedServiceStatus GetOrCreateServiceStatus(string serviceName)
    {
        return _serviceStatuses.GetOrAdd(serviceName, _ => new EnhancedServiceStatus());
    }

    private void UpdateCircuitBreakerState(string serviceName, EnhancedServiceStatus status)
    {
        var currentState = _circuitStates.GetValueOrDefault(serviceName, CircuitBreakerState.Closed);
        
        // Open circuit if too many consecutive failures
        if (status.ConsecutiveFailures >= 5 && currentState == CircuitBreakerState.Closed)
        {
            _circuitStates[serviceName] = CircuitBreakerState.Open;
            _logger.LogWarning("Circuit breaker opened for service {ServiceName} due to {FailureCount} consecutive failures", 
                serviceName, status.ConsecutiveFailures);
        }
        // Half-open after some time
        else if (currentState == CircuitBreakerState.Open && 
                 status.LastFailure.HasValue && 
                 DateTime.UtcNow - status.LastFailure.Value > TimeSpan.FromMinutes(1))
        {
            _circuitStates[serviceName] = CircuitBreakerState.HalfOpen;
            _logger.LogInformation("Circuit breaker half-opened for service {ServiceName}", serviceName);
        }
        // Close circuit on success in half-open state
        else if (currentState == CircuitBreakerState.HalfOpen && status.ConsecutiveFailures == 0)
        {
            _circuitStates[serviceName] = CircuitBreakerState.Closed;
            _logger.LogInformation("Circuit breaker closed for service {ServiceName}", serviceName);
        }
    }

    private void InitializeRecoveryStrategies()
    {
        _recoveryStrategies["exchange_rate_service"] = new ExchangeRateRecoveryStrategy();
        _recoveryStrategies["blockchain"] = new BlockchainRecoveryStrategy();
        _recoveryStrategies["database"] = new DatabaseRecoveryStrategy();
        _recoveryStrategies["redis"] = new RedisRecoveryStrategy();
    }

    private async void UpdateServiceStatuses(object? state)
    {
        try
        {
            var healthReport = await _healthCheckService.CheckHealthAsync();
            
            foreach (var entry in healthReport.Entries)
            {
                var isAvailable = entry.Value.Status == HealthStatus.Healthy;
                var degradationLevel = MapHealthStatusToDegradationLevel(entry.Value.Status);
                
                var status = GetOrCreateServiceStatus(entry.Key);
                
                if (!status.IsManuallyUnavailable)
                {
                    status.IsAvailable = isAvailable;
                }
                
                status.HealthStatus = entry.Value.Status;
                status.DegradationLevel = degradationLevel;
                status.LastHealthCheck = DateTime.UtcNow;
                
                if (isAvailable)
                {
                    status.LastSuccessfulCheck = DateTime.UtcNow;
                    status.RecordSuccess();
                }
                else
                {
                    status.RecordFailure();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service statuses from health checks");
        }
    }

    private async void AttemptServiceRecovery(object? state)
    {
        var degradedServices = _serviceStatuses
            .Where(s => s.Value.DegradationLevel > DegradationLevel.Healthy)
            .Select(s => s.Key)
            .ToList();
            
        foreach (var serviceName in degradedServices)
        {
            try
            {
                await TryRecoverServiceAsync(serviceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during automatic recovery attempt for service {ServiceName}", serviceName);
            }
        }
    }

    private DegradationLevel MapHealthStatusToDegradationLevel(HealthStatus healthStatus)
    {
        return healthStatus switch
        {
            HealthStatus.Healthy => DegradationLevel.Healthy,
            HealthStatus.Degraded => DegradationLevel.Degraded,
            HealthStatus.Unhealthy => DegradationLevel.Critical,
            _ => DegradationLevel.Unknown
        };
    }

    private string CalculateOverallHealth(List<DetailedDegradedService> services)
    {
        if (services.Count == 0) return "Healthy";
        
        var criticalCount = services.Count(s => s.DegradationLevel == DegradationLevel.Critical);
        var degradedCount = services.Count(s => s.DegradationLevel == DegradationLevel.Degraded);
        
        if (criticalCount > 0) return "Critical";
        if (degradedCount > 0) return "Degraded";
        return "Healthy";
    }

    private TimeSpan CalculateEstimatedRecoveryTime(EnhancedServiceStatus status)
    {
        return status.DegradationLevel switch
        {
            DegradationLevel.Degraded => TimeSpan.FromMinutes(5),
            DegradationLevel.Critical => TimeSpan.FromMinutes(15),
            _ => TimeSpan.Zero
        };
    }

    private List<string> GenerateRecoveryActions(string serviceName, EnhancedServiceStatus status)
    {
        var actions = new List<string>();
        
        switch (status.DegradationLevel)
        {
            case DegradationLevel.Degraded:
                actions.Add("Monitor service performance");
                actions.Add("Check for high latency or timeout issues");
                break;
                
            case DegradationLevel.Critical:
                actions.Add("Check service connectivity");
                actions.Add("Verify service configuration");
                actions.Add("Restart service if necessary");
                actions.Add("Check resource availability (CPU, memory, disk)");
                break;
        }
        
        return actions;
    }

    public void Dispose()
    {
        _statusUpdateTimer?.Dispose();
        _recoveryTimer?.Dispose();
    }
}

// Enhanced service status with more detailed tracking
public class EnhancedServiceStatus
{
    public bool IsAvailable { get; set; } = true;
    public bool IsManuallyUnavailable { get; set; } = false;
    public DateTime? UnavailableUntil { get; set; }
    public HealthStatus? HealthStatus { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public DateTime? LastSuccessfulCheck { get; set; }
    public DegradationLevel DegradationLevel { get; set; } = DegradationLevel.Healthy;
    
    // Failure tracking
    public int ConsecutiveFailures { get; set; } = 0;
    public DateTime? LastFailure { get; set; }
    public DateTime? LastSuccess { get; set; }
    
    // Performance metrics
    private readonly Queue<bool> _recentResults = new(); // true = success, false = failure
    private const int MaxRecentResults = 20;
    
    public void RecordSuccess()
    {
        ConsecutiveFailures = 0;
        LastSuccess = DateTime.UtcNow;
        
        _recentResults.Enqueue(true);
        if (_recentResults.Count > MaxRecentResults)
        {
            _recentResults.Dequeue();
        }
    }
    
    public void RecordFailure()
    {
        ConsecutiveFailures++;
        LastFailure = DateTime.UtcNow;
        
        _recentResults.Enqueue(false);
        if (_recentResults.Count > MaxRecentResults)
        {
            _recentResults.Dequeue();
        }
    }
    
    public double GetSuccessRate()
    {
        if (_recentResults.Count == 0) return 1.0;
        return (double)_recentResults.Count(x => x) / _recentResults.Count;
    }
}

// Degradation levels
public enum DegradationLevel
{
    Healthy = 0,
    Degraded = 1,
    Critical = 2,
    Unknown = 3
}

// Enhanced degraded service information
public class DetailedDegradedService : DegradedService
{
    public DegradationLevel DegradationLevel { get; set; }
    public CircuitBreakerState CircuitBreakerState { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate { get; set; }
    public ServiceRecoveryPlan? RecoveryPlan { get; set; }
}

// Service recovery plan
public class ServiceRecoveryPlan
{
    public string ServiceName { get; set; } = "";
    public bool IsHealthy { get; set; }
    public DegradationLevel DegradationLevel { get; set; }
    public TimeSpan EstimatedRecoveryTime { get; set; }
    public List<string> RecommendedActions { get; set; } = new();
    public bool CanAutoRecover { get; set; }
    public DateTime? NextRecoveryAttempt { get; set; }
}

// Recovery strategy interface and implementations
public interface IServiceRecoveryStrategy
{
    Task<bool> TryRecoverAsync(string serviceName);
}

public class DefaultRecoveryStrategy : IServiceRecoveryStrategy
{
    public async Task<bool> TryRecoverAsync(string serviceName)
    {
        // Default recovery just waits and checks again
        await Task.Delay(TimeSpan.FromSeconds(5));
        return false; // Cannot auto-recover without specific strategy
    }
}

public class ExchangeRateRecoveryStrategy : IServiceRecoveryStrategy
{
    public async Task<bool> TryRecoverAsync(string serviceName)
    {
        // For exchange rate service, we could try switching to backup API
        // or clearing cache, etc.
        await Task.Delay(TimeSpan.FromSeconds(2));
        return true; // Assume recovery succeeded for demo
    }
}

public class BlockchainRecoveryStrategy : IServiceRecoveryStrategy
{
    public async Task<bool> TryRecoverAsync(string serviceName)
    {
        // For blockchain services, might try reconnecting or switching nodes
        await Task.Delay(TimeSpan.FromSeconds(5));
        return true;
    }
}

public class DatabaseRecoveryStrategy : IServiceRecoveryStrategy
{
    public async Task<bool> TryRecoverAsync(string serviceName)
    {
        // For database, might try reconnecting or checking connection pool
        await Task.Delay(TimeSpan.FromSeconds(3));
        return true;
    }
}

public class RedisRecoveryStrategy : IServiceRecoveryStrategy
{
    public async Task<bool> TryRecoverAsync(string serviceName)
    {
        // For Redis, might try reconnecting or clearing corrupted data
        await Task.Delay(TimeSpan.FromSeconds(1));
        return true;
    }
}

// Custom exception for service unavailability
public class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message) : base(message) { }
    public ServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}