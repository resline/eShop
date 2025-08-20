using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using CryptoPayment.API.Models;
using CryptoPayment.API.Services;

namespace CryptoPayment.API.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IErrorHandlingService _errorHandlingService;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IWebHostEnvironment environment,
        IErrorHandlingService errorHandlingService)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _errorHandlingService = errorHandlingService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var categorizedError = _errorHandlingService.CategorizeError(exception);
            
            // Log the exception with correlation ID and additional context
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["UserId"] = context.User?.Identity?.Name ?? "Anonymous",
                ["ClientIp"] = GetClientIpAddress(context),
                ["UserAgent"] = context.Request.Headers["User-Agent"].ToString(),
                ["RequestPath"] = context.Request.Path,
                ["RequestMethod"] = context.Request.Method,
                ["ErrorCategory"] = categorizedError.Category.ToString(),
                ["IsRetryable"] = categorizedError.IsRetryable
            });

            switch (categorizedError.Category)
            {
                case ErrorCategory.Security:
                    _logger.LogWarning(exception, "Security-related error occurred");
                    break;
                case ErrorCategory.Validation:
                    _logger.LogInformation(exception, "Validation error occurred");
                    break;
                case ErrorCategory.External:
                    _logger.LogWarning(exception, "External service error occurred");
                    break;
                case ErrorCategory.Transient:
                    _logger.LogInformation(exception, "Transient error occurred");
                    break;
                default:
                    _logger.LogError(exception, "Unhandled exception occurred");
                    break;
            }

            // Create response based on error category
            var response = await CreateErrorResponseAsync(categorizedError, correlationId, context);
            
            context.Response.Clear();
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = "application/json";
            
            await context.Response.WriteAsync(JsonSerializer.Serialize(response.ProblemDetails, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
        catch (Exception handlingException)
        {
            // Fallback error handling
            _logger.LogCritical(handlingException, "Exception occurred while handling another exception. CorrelationId: {CorrelationId}", correlationId);
            
            await WriteFallbackErrorResponse(context, correlationId);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("Exception handling completed in {ElapsedMilliseconds}ms for CorrelationId: {CorrelationId}", 
                stopwatch.ElapsedMilliseconds, correlationId);
        }
    }

    private async Task<ErrorResponse> CreateErrorResponseAsync(CategorizedError categorizedError, string correlationId, HttpContext context)
    {
        var statusCode = DetermineStatusCode(categorizedError);
        
        var problemDetails = new ProblemDetails
        {
            Type = $"https://docs.cryptopayment.api/errors/{categorizedError.Category.ToString().ToLowerInvariant()}",
            Title = GetErrorTitle(categorizedError),
            Status = (int)statusCode,
            Detail = await GetErrorDetailAsync(categorizedError, context),
            Instance = context.Request.Path
        };

        // Add correlation ID for tracking
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["errorId"] = Guid.NewGuid().ToString();
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        // Add retry information for transient errors
        if (categorizedError.IsRetryable)
        {
            problemDetails.Extensions["retryable"] = true;
            problemDetails.Extensions["retryAfterSeconds"] = categorizedError.RetryAfterSeconds;
        }

        // Add additional context in development
        if (_environment.IsDevelopment() && categorizedError.Exception != null)
        {
            problemDetails.Extensions["stackTrace"] = categorizedError.Exception.ToString();
            problemDetails.Extensions["innerException"] = categorizedError.Exception.InnerException?.Message;
        }

        // Add help link for common errors
        if (!string.IsNullOrEmpty(categorizedError.HelpLink))
        {
            problemDetails.Extensions["helpLink"] = categorizedError.HelpLink;
        }

        return new ErrorResponse { StatusCode = statusCode, ProblemDetails = problemDetails };
    }

    private HttpStatusCode DetermineStatusCode(CategorizedError categorizedError) => categorizedError.Category switch
    {
        ErrorCategory.Validation => HttpStatusCode.BadRequest,
        ErrorCategory.Security => HttpStatusCode.Forbidden,
        ErrorCategory.NotFound => HttpStatusCode.NotFound,
        ErrorCategory.Conflict => HttpStatusCode.Conflict,
        ErrorCategory.External => HttpStatusCode.BadGateway,
        ErrorCategory.Transient => HttpStatusCode.ServiceUnavailable,
        ErrorCategory.RateLimited => HttpStatusCode.TooManyRequests,
        _ => HttpStatusCode.InternalServerError
    };

    private string GetErrorTitle(CategorizedError categorizedError) => categorizedError.Category switch
    {
        ErrorCategory.Validation => "Invalid Request",
        ErrorCategory.Security => "Access Forbidden",
        ErrorCategory.NotFound => "Resource Not Found",
        ErrorCategory.Conflict => "Conflict",
        ErrorCategory.External => "External Service Error",
        ErrorCategory.Transient => "Service Temporarily Unavailable",
        ErrorCategory.RateLimited => "Rate Limit Exceeded",
        _ => "Internal Server Error"
    };

    private async Task<string> GetErrorDetailAsync(CategorizedError categorizedError, HttpContext context)
    {
        if (!string.IsNullOrEmpty(categorizedError.UserFriendlyMessage))
        {
            return categorizedError.UserFriendlyMessage;
        }

        return categorizedError.Category switch
        {
            ErrorCategory.Validation => "The request contains invalid data. Please check your input and try again.",
            ErrorCategory.Security => "You don't have permission to access this resource.",
            ErrorCategory.NotFound => "The requested resource could not be found.",
            ErrorCategory.Conflict => "The request conflicts with the current state of the resource.",
            ErrorCategory.External => "An external service is temporarily unavailable. Please try again later.",
            ErrorCategory.Transient => "The service is temporarily unavailable. Please try again in a few moments.",
            ErrorCategory.RateLimited => "Too many requests. Please slow down and try again later.",
            _ => "An unexpected error occurred. Please try again or contact support if the problem persists."
        };
    }

    private async Task WriteFallbackErrorResponse(HttpContext context, string correlationId)
    {
        try
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                var fallbackResponse = new
                {
                    type = "https://docs.cryptopayment.api/errors/internal",
                    title = "Internal Server Error",
                    status = 500,
                    detail = "An unexpected error occurred. Please try again or contact support.",
                    correlationId = correlationId,
                    timestamp = DateTimeOffset.UtcNow
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(fallbackResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
            }
        }
        catch (Exception fallbackException)
        {
            _logger.LogCritical(fallbackException, "Failed to write fallback error response. CorrelationId: {CorrelationId}", correlationId);
        }
    }

    private string GetOrCreateCorrelationId(HttpContext context)
    {
        const string correlationIdHeader = "X-Correlation-ID";
        
        if (context.Request.Headers.TryGetValue(correlationIdHeader, out var correlationId) &&
            !string.IsNullOrEmpty(correlationId))
        {
            return correlationId!;
        }

        var newCorrelationId = Guid.NewGuid().ToString();
        context.Response.Headers.Add(correlationIdHeader, newCorrelationId);
        return newCorrelationId;
    }

    private string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers first
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private record ErrorResponse
    {
        public HttpStatusCode StatusCode { get; init; }
        public ProblemDetails ProblemDetails { get; init; } = new();
    }
}

// Extension method for easy registration
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionMiddleware>();
    }
}