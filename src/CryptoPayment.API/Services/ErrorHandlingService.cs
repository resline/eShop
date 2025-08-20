using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using CryptoPayment.API.Models;
using CryptoPayment.API.Exceptions;
using CryptoPayment.BlockchainServices.Exceptions;

namespace CryptoPayment.API.Services;

public class ErrorHandlingService : IErrorHandlingService
{
    private readonly ILogger<ErrorHandlingService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, ErrorCategory> _errorCategoryMapping;
    private readonly Dictionary<ErrorCategory, int> _defaultRetryDelays;

    public ErrorHandlingService(ILogger<ErrorHandlingService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _errorCategoryMapping = InitializeErrorCategoryMapping();
        _defaultRetryDelays = InitializeRetryDelays();
    }

    public CategorizedError CategorizeError(Exception exception)
    {
        var category = DetermineErrorCategory(exception);
        var isRetryable = DetermineIfRetryable(exception, category);
        var retryAfterSeconds = GetRetryAfterSeconds(category);
        var helpLink = GetHelpLink(category, exception);
        var userFriendlyMessage = GetBasicUserFriendlyMessage(exception, category);

        var metadata = ExtractErrorMetadata(exception);

        return new CategorizedError
        {
            Category = category,
            Exception = exception,
            UserFriendlyMessage = userFriendlyMessage,
            IsRetryable = isRetryable,
            RetryAfterSeconds = retryAfterSeconds,
            HelpLink = helpLink,
            Metadata = metadata
        };
    }

    public async Task<string> GetUserFriendlyMessageAsync(Exception exception, string? languageCode = null)
    {
        try
        {
            // Try to get localized message
            var localizer = _serviceProvider.GetService<IStringLocalizer>();
            if (localizer != null && !string.IsNullOrEmpty(languageCode))
            {
                var localizedMessage = GetLocalizedMessage(exception, localizer, languageCode);
                if (!string.IsNullOrEmpty(localizedMessage))
                {
                    return localizedMessage;
                }
            }

            // Fallback to basic user-friendly message
            var category = DetermineErrorCategory(exception);
            return GetBasicUserFriendlyMessage(exception, category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user-friendly message for exception: {ExceptionType}", exception.GetType().Name);
            return "An unexpected error occurred. Please try again or contact support.";
        }
    }

    public bool ShouldRetry(Exception exception, int attemptCount)
    {
        const int maxRetryAttempts = 3;
        
        if (attemptCount >= maxRetryAttempts)
            return false;

        var category = DetermineErrorCategory(exception);
        
        return category switch
        {
            ErrorCategory.Transient => true,
            ErrorCategory.External => attemptCount < 2, // Only retry once for external services
            ErrorCategory.RateLimited => attemptCount < 2,
            _ => false
        };
    }

    public async Task RecordErrorMetricsAsync(Exception exception, string correlationId, string? userId = null)
    {
        try
        {
            var category = DetermineErrorCategory(exception);
            var errorType = exception.GetType().Name;

            // Log structured metrics for monitoring systems
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["MetricType"] = "ErrorOccurrence",
                ["ErrorCategory"] = category.ToString(),
                ["ErrorType"] = errorType,
                ["CorrelationId"] = correlationId,
                ["UserId"] = userId ?? "Anonymous",
                ["Timestamp"] = DateTimeOffset.UtcNow
            });

            _logger.LogInformation("Error metric recorded: {ErrorCategory}.{ErrorType}", category, errorType);

            // Here you could also send metrics to external monitoring systems
            // await SendToMonitoringSystem(category, errorType, correlationId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record error metrics for CorrelationId: {CorrelationId}", correlationId);
        }
    }

    public TimeSpan GetRetryDelay(Exception exception, int attemptCount)
    {
        var category = DetermineErrorCategory(exception);
        var baseDelay = _defaultRetryDelays.GetValueOrDefault(category, 1);
        
        // Exponential backoff with jitter
        var delaySeconds = Math.Min(baseDelay * Math.Pow(2, attemptCount), 60); // Max 60 seconds
        var jitter = Random.Shared.NextDouble() * 0.1; // Add up to 10% jitter
        
        return TimeSpan.FromSeconds(delaySeconds * (1 + jitter));
    }

    private ErrorCategory DetermineErrorCategory(Exception exception)
    {
        // Check explicit category mapping first
        if (_errorCategoryMapping.TryGetValue(exception.GetType(), out var mappedCategory))
        {
            return mappedCategory;
        }

        // Check inheritance hierarchy
        var exceptionType = exception.GetType();
        foreach (var (type, category) in _errorCategoryMapping)
        {
            if (type.IsAssignableFrom(exceptionType))
            {
                return category;
            }
        }

        // Check by exception properties and content
        return exception switch
        {
            HttpRequestException httpEx when httpEx.Message.Contains("timeout") => ErrorCategory.Transient,
            HttpRequestException httpEx when httpEx.Message.Contains("SSL") => ErrorCategory.Security,
            TaskCanceledException => ErrorCategory.Transient,
            TimeoutException => ErrorCategory.Transient,
            SocketException => ErrorCategory.External,
            JsonException => ErrorCategory.Validation,
            _ when exception.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => ErrorCategory.RateLimited,
            _ when exception.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Security,
            _ when exception.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Security,
            _ when exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) => ErrorCategory.NotFound,
            _ => ErrorCategory.Unknown
        };
    }

    private bool DetermineIfRetryable(Exception exception, ErrorCategory category)
    {
        // Never retry security or validation errors
        if (category is ErrorCategory.Security or ErrorCategory.Validation)
            return false;

        // Check specific exceptions
        return exception switch
        {
            TaskCanceledException => true,
            TimeoutException => true,
            HttpRequestException httpEx when IsRetryableHttpStatus(httpEx) => true,
            SocketException => true,
            _ => category is ErrorCategory.Transient or ErrorCategory.External or ErrorCategory.RateLimited
        };
    }

    private bool IsRetryableHttpStatus(HttpRequestException httpException)
    {
        // Check if the exception message contains retryable status codes
        var message = httpException.Message.ToLowerInvariant();
        return message.Contains("500") || // Internal Server Error
               message.Contains("502") || // Bad Gateway
               message.Contains("503") || // Service Unavailable
               message.Contains("504") || // Gateway Timeout
               message.Contains("429");   // Too Many Requests
    }

    private int GetRetryAfterSeconds(ErrorCategory category)
    {
        return category switch
        {
            ErrorCategory.RateLimited => 60,
            ErrorCategory.External => 30,
            ErrorCategory.Transient => 5,
            _ => 0
        };
    }

    private string? GetHelpLink(ErrorCategory category, Exception exception)
    {
        return category switch
        {
            ErrorCategory.Validation => "https://docs.cryptopayment.api/validation-errors",
            ErrorCategory.Security => "https://docs.cryptopayment.api/security-errors",
            ErrorCategory.RateLimited => "https://docs.cryptopayment.api/rate-limits",
            ErrorCategory.External => "https://docs.cryptopayment.api/external-service-errors",
            _ => "https://docs.cryptopayment.api/general-errors"
        };
    }

    private string GetBasicUserFriendlyMessage(Exception exception, ErrorCategory category)
    {
        // Check for user-friendly exception types first
        if (exception is UserFriendlyException userFriendlyEx)
        {
            return userFriendlyEx.UserMessage;
        }

        if (exception is CryptoPaymentBusinessException businessEx)
        {
            return businessEx.UserMessage;
        }

        // Fallback to category-based messages
        return category switch
        {
            ErrorCategory.Validation => "The provided information is invalid. Please check your input and try again.",
            ErrorCategory.Security => "You don't have permission to perform this action.",
            ErrorCategory.NotFound => "The requested resource could not be found.",
            ErrorCategory.Conflict => "This operation conflicts with the current state. Please refresh and try again.",
            ErrorCategory.External => "An external service is temporarily unavailable. Please try again in a few minutes.",
            ErrorCategory.Transient => "The service is temporarily busy. Please try again in a moment.",
            ErrorCategory.RateLimited => "You're making requests too quickly. Please slow down and try again.",
            ErrorCategory.Business => "This operation cannot be completed due to business rules.",
            ErrorCategory.Configuration => "The service is temporarily misconfigured. Please contact support.",
            _ => "An unexpected error occurred. Please try again or contact support if the problem persists."
        };
    }

    private string? GetLocalizedMessage(Exception exception, IStringLocalizer localizer, string languageCode)
    {
        try
        {
            var key = $"Error.{exception.GetType().Name}";
            var localizedString = localizer[key];
            
            if (!localizedString.ResourceNotFound)
            {
                return localizedString.Value;
            }

            // Try category-based key
            var category = DetermineErrorCategory(exception);
            var categoryKey = $"Error.Category.{category}";
            var categoryLocalizedString = localizer[categoryKey];
            
            return categoryLocalizedString.ResourceNotFound ? null : categoryLocalizedString.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting localized message for {ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    private Dictionary<string, object> ExtractErrorMetadata(Exception exception)
    {
        var metadata = new Dictionary<string, object>
        {
            ["ExceptionType"] = exception.GetType().Name,
            ["Source"] = exception.Source ?? "Unknown"
        };

        // Add specific metadata based on exception type
        switch (exception)
        {
            case HttpRequestException httpEx:
                metadata["HttpMethod"] = "Unknown"; // Would need to extract from context
                break;
            case ArgumentException argEx:
                metadata["ParameterName"] = argEx.ParamName ?? "Unknown";
                break;
            case InvalidOperationException invalidOpEx:
                metadata["OperationContext"] = "Unknown"; // Would need to extract from context
                break;
        }

        return metadata;
    }

    private Dictionary<Type, ErrorCategory> InitializeErrorCategoryMapping()
    {
        return new Dictionary<Type, ErrorCategory>
        {
            // Validation errors
            [typeof(ArgumentException)] = ErrorCategory.Validation,
            [typeof(ArgumentNullException)] = ErrorCategory.Validation,
            [typeof(ArgumentOutOfRangeException)] = ErrorCategory.Validation,
            [typeof(FormatException)] = ErrorCategory.Validation,
            [typeof(JsonException)] = ErrorCategory.Validation,

            // Security errors
            [typeof(UnauthorizedAccessException)] = ErrorCategory.Security,
            [typeof(SecurityException)] = ErrorCategory.Security,

            // Not found errors
            [typeof(FileNotFoundException)] = ErrorCategory.NotFound,
            [typeof(DirectoryNotFoundException)] = ErrorCategory.NotFound,
            [typeof(KeyNotFoundException)] = ErrorCategory.NotFound,

            // Conflict errors
            [typeof(InvalidOperationException)] = ErrorCategory.Conflict,

            // External service errors
            [typeof(HttpRequestException)] = ErrorCategory.External,
            [typeof(SocketException)] = ErrorCategory.External,

            // Transient errors
            [typeof(TaskCanceledException)] = ErrorCategory.Transient,
            [typeof(TimeoutException)] = ErrorCategory.Transient,
            [typeof(OperationCanceledException)] = ErrorCategory.Transient,

            // Business errors (custom exceptions)
            [typeof(CryptoPaymentBusinessException)] = ErrorCategory.Business,
            [typeof(InsufficientFundsException)] = ErrorCategory.Business,
            [typeof(InvalidAddressException)] = ErrorCategory.Validation,
            [typeof(BlockchainException)] = ErrorCategory.External,
            [typeof(TransactionTimeoutException)] = ErrorCategory.Transient,

            // Configuration errors
            [typeof(ConfigurationException)] = ErrorCategory.Configuration
        };
    }

    private Dictionary<ErrorCategory, int> InitializeRetryDelays()
    {
        return new Dictionary<ErrorCategory, int>
        {
            [ErrorCategory.Transient] = 1,    // 1 second base delay
            [ErrorCategory.External] = 5,     // 5 seconds base delay
            [ErrorCategory.RateLimited] = 60, // 60 seconds base delay
            [ErrorCategory.Configuration] = 30 // 30 seconds base delay
        };
    }
}