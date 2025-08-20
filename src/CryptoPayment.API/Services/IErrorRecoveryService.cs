namespace CryptoPayment.API.Services;

public interface IErrorRecoveryService
{
    /// <summary>
    /// Executes an operation with automatic retry logic
    /// </summary>
    Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation, 
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation with automatic retry logic (void return)
    /// </summary>
    Task ExecuteWithRetryAsync(
        Func<Task> operation, 
        RetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation with graceful degradation fallback
    /// </summary>
    Task<T> ExecuteWithFallbackAsync<T>(
        Func<Task<T>> primaryOperation,
        Func<Task<T>> fallbackOperation,
        Func<Exception, bool>? shouldFallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes compensation action for failed operations
    /// </summary>
    Task ExecuteCompensationAsync(
        string operationId,
        Func<Task> compensationAction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a compensation action for later execution
    /// </summary>
    void RegisterCompensationAction(string operationId, Func<Task> compensationAction);

    /// <summary>
    /// Circuit breaker for external services
    /// </summary>
    Task<T> ExecuteWithCircuitBreakerAsync<T>(
        string serviceName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a circuit breaker
    /// </summary>
    CircuitBreakerState GetCircuitBreakerState(string serviceName);
}

public record RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(1);
    public double BackoffMultiplier { get; init; } = 2.0;
    public double JitterFactor { get; init; } = 0.1;
    public Func<Exception, bool>? ShouldRetry { get; init; }
    public Func<int, Exception, TimeSpan>? DelayCalculator { get; init; }
}

public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Failing fast
    HalfOpen   // Testing if service is back
}

public record CircuitBreakerConfig
{
    public int FailureThreshold { get; init; } = 5;
    public TimeSpan OpenTimeout { get; init; } = TimeSpan.FromMinutes(1);
    public int HalfOpenMaxCalls { get; init; } = 3;
    public double FailureRateThreshold { get; init; } = 0.5; // 50%
}