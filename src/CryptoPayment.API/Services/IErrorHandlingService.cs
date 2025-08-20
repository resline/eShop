using CryptoPayment.API.Models;

namespace CryptoPayment.API.Services;

public interface IErrorHandlingService
{
    /// <summary>
    /// Categorizes an exception into predefined error categories
    /// </summary>
    CategorizedError CategorizeError(Exception exception);

    /// <summary>
    /// Gets a user-friendly error message for the given exception
    /// </summary>
    Task<string> GetUserFriendlyMessageAsync(Exception exception, string? languageCode = null);

    /// <summary>
    /// Determines if an error should be retried automatically
    /// </summary>
    bool ShouldRetry(Exception exception, int attemptCount);

    /// <summary>
    /// Records error metrics for monitoring and alerting
    /// </summary>
    Task RecordErrorMetricsAsync(Exception exception, string correlationId, string? userId = null);

    /// <summary>
    /// Gets recommended retry delay for transient errors
    /// </summary>
    TimeSpan GetRetryDelay(Exception exception, int attemptCount);
}

public enum ErrorCategory
{
    Unknown,
    Validation,
    Security,
    NotFound,
    Conflict,
    External,
    Transient,
    RateLimited,
    Business,
    Configuration
}

public record CategorizedError
{
    public ErrorCategory Category { get; init; }
    public Exception? Exception { get; init; }
    public string? UserFriendlyMessage { get; init; }
    public bool IsRetryable { get; init; }
    public int RetryAfterSeconds { get; init; }
    public string? HelpLink { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}