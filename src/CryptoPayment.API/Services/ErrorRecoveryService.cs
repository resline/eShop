using System.Collections.Concurrent;
using System.Diagnostics;
using CryptoPayment.API.Exceptions;

namespace CryptoPayment.API.Services;

public class ErrorRecoveryService : IErrorRecoveryService
{
    private readonly ILogger<ErrorRecoveryService> _logger;
    private readonly IErrorHandlingService _errorHandlingService;
    private readonly ConcurrentDictionary<string, Func<Task>> _compensationActions = new();
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers = new();

    public ErrorRecoveryService(
        ILogger<ErrorRecoveryService> logger,
        IErrorHandlingService errorHandlingService)
    {
        _logger = logger;
        _errorHandlingService = errorHandlingService;
    }

    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation, 
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        retryPolicy ??= new RetryPolicy();
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < retryPolicy.MaxAttempts)
        {
            try
            {
                var result = await operation();
                
                if (attempt > 0)
                {
                    _logger.LogInformation("Operation succeeded on attempt {Attempt}", attempt + 1);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                var shouldRetry = retryPolicy.ShouldRetry?.Invoke(ex) ?? 
                                 _errorHandlingService.ShouldRetry(ex, attempt);

                if (!shouldRetry || attempt >= retryPolicy.MaxAttempts)
                {
                    _logger.LogWarning(ex, "Operation failed after {Attempts} attempts. Will not retry.", attempt);
                    break;
                }

                var delay = CalculateDelay(retryPolicy, attempt, ex);
                
                _logger.LogInformation(ex, 
                    "Operation failed on attempt {Attempt}. Retrying in {Delay}ms", 
                    attempt, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed without exception");
    }

    public async Task ExecuteWithRetryAsync(
        Func<Task> operation, 
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true; // Dummy return value
        }, retryPolicy, cancellationToken);
    }

    public async Task<T> ExecuteWithFallbackAsync<T>(
        Func<Task<T>> primaryOperation,
        Func<Task<T>> fallbackOperation,
        Func<Exception, bool>? shouldFallback = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await primaryOperation();
        }
        catch (Exception ex)
        {
            var useFallback = shouldFallback?.Invoke(ex) ?? ShouldUseFallback(ex);
            
            if (!useFallback)
            {
                throw;
            }

            _logger.LogWarning(ex, "Primary operation failed, executing fallback");

            try
            {
                var result = await fallbackOperation();
                _logger.LogInformation("Fallback operation succeeded");
                return result;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallback operation also failed");
                
                // Throw aggregate exception with both failures
                throw new AggregateException("Both primary and fallback operations failed", ex, fallbackEx);
            }
        }
    }

    public async Task ExecuteCompensationAsync(
        string operationId,
        Func<Task> compensationAction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing compensation action for operation {OperationId}", operationId);
            
            await compensationAction();
            
            _logger.LogInformation("Compensation action completed successfully for operation {OperationId}", operationId);
            
            // Remove the compensation action after successful execution
            _compensationActions.TryRemove(operationId, out _);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compensation action failed for operation {OperationId}", operationId);
            
            // Store for retry later
            RegisterCompensationAction(operationId, compensationAction);
            throw;
        }
    }

    public void RegisterCompensationAction(string operationId, Func<Task> compensationAction)
    {
        _compensationActions.AddOrUpdate(operationId, compensationAction, (_, _) => compensationAction);
        _logger.LogDebug("Registered compensation action for operation {OperationId}", operationId);
    }

    public async Task<T> ExecuteWithCircuitBreakerAsync<T>(
        string serviceName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var circuitBreaker = GetOrCreateCircuitBreaker(serviceName);
        return await circuitBreaker.ExecuteAsync(operation, cancellationToken);
    }

    public CircuitBreakerState GetCircuitBreakerState(string serviceName)
    {
        if (_circuitBreakers.TryGetValue(serviceName, out var circuitBreaker))
        {
            return circuitBreaker.State;
        }
        
        return CircuitBreakerState.Closed; // Default state
    }

    private TimeSpan CalculateDelay(RetryPolicy retryPolicy, int attempt, Exception exception)
    {
        if (retryPolicy.DelayCalculator != null)
        {
            return retryPolicy.DelayCalculator(attempt, exception);
        }

        // Exponential backoff with jitter
        var delay = TimeSpan.FromMilliseconds(
            retryPolicy.BaseDelay.TotalMilliseconds * Math.Pow(retryPolicy.BackoffMultiplier, attempt - 1));

        // Apply maximum delay
        if (delay > retryPolicy.MaxDelay)
        {
            delay = retryPolicy.MaxDelay;
        }

        // Add jitter to prevent thundering herd
        var jitter = Random.Shared.NextDouble() * retryPolicy.JitterFactor;
        var jitterMs = delay.TotalMilliseconds * jitter;
        
        return delay.Add(TimeSpan.FromMilliseconds(jitterMs));
    }

    private bool ShouldUseFallback(Exception exception)
    {
        var categorizedError = _errorHandlingService.CategorizeError(exception);
        
        return categorizedError.Category switch
        {
            ErrorCategory.External => true,
            ErrorCategory.Transient => true,
            ErrorCategory.RateLimited => true,
            _ => false
        };
    }

    private CircuitBreaker GetOrCreateCircuitBreaker(string serviceName)
    {
        return _circuitBreakers.GetOrAdd(serviceName, _ => new CircuitBreaker(
            serviceName, 
            new CircuitBreakerConfig(), 
            _logger));
    }
}

// Circuit Breaker implementation
internal class CircuitBreaker
{
    private readonly string _serviceName;
    private readonly CircuitBreakerConfig _config;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private int _successCount = 0;
    private int _totalCalls = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private DateTime _lastStateChangeTime = DateTime.UtcNow;

    public CircuitBreakerState State => _state;

    public CircuitBreaker(string serviceName, CircuitBreakerConfig config, ILogger logger)
    {
        _serviceName = serviceName;
        _config = config;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                if (DateTime.UtcNow - _lastStateChangeTime < _config.OpenTimeout)
                {
                    throw new ExternalServiceException(_serviceName, "Circuit breaker is open");
                }
                
                // Transition to half-open
                _state = CircuitBreakerState.HalfOpen;
                _successCount = 0;
                _totalCalls = 0;
                _lastStateChangeTime = DateTime.UtcNow;
                
                _logger.LogInformation("Circuit breaker for {ServiceName} transitioned to Half-Open", _serviceName);
            }
            
            if (_state == CircuitBreakerState.HalfOpen && _totalCalls >= _config.HalfOpenMaxCalls)
            {
                throw new ExternalServiceException(_serviceName, "Circuit breaker is in half-open state with max calls reached");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await operation();
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("Circuit breaker operation for {ServiceName} completed in {Duration}ms", 
                _serviceName, stopwatch.ElapsedMilliseconds);
        }
    }

    private void OnSuccess()
    {
        lock (_lock)
        {
            _successCount++;
            _totalCalls++;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                if (_successCount >= _config.HalfOpenMaxCalls)
                {
                    // Transition back to closed
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                    _successCount = 0;
                    _totalCalls = 0;
                    _lastStateChangeTime = DateTime.UtcNow;
                    
                    _logger.LogInformation("Circuit breaker for {ServiceName} transitioned to Closed", _serviceName);
                }
            }
            else if (_state == CircuitBreakerState.Closed)
            {
                // Reset failure count on success
                _failureCount = 0;
            }
        }
    }

    private void OnFailure(Exception exception)
    {
        lock (_lock)
        {
            _failureCount++;
            _totalCalls++;
            _lastFailureTime = DateTime.UtcNow;

            if (_state == CircuitBreakerState.Closed)
            {
                if (_failureCount >= _config.FailureThreshold)
                {
                    // Transition to open
                    _state = CircuitBreakerState.Open;
                    _lastStateChangeTime = DateTime.UtcNow;
                    
                    _logger.LogWarning(exception, 
                        "Circuit breaker for {ServiceName} transitioned to Open after {FailureCount} failures", 
                        _serviceName, _failureCount);
                }
            }
            else if (_state == CircuitBreakerState.HalfOpen)
            {
                // Any failure in half-open state transitions back to open
                _state = CircuitBreakerState.Open;
                _lastStateChangeTime = DateTime.UtcNow;
                
                _logger.LogWarning(exception, 
                    "Circuit breaker for {ServiceName} transitioned back to Open from Half-Open", 
                    _serviceName);
            }
        }
    }
}