using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace CryptoPayment.API.Middleware;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddCryptoPaymentRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Global rate limiter - 100 requests per minute per IP
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIdentifier(httpContext),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // API endpoint specific policies
            options.AddPolicy("api", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIdentifier(httpContext),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 50,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Webhook endpoints - more restrictive
            options.AddPolicy("webhook", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIdentifier(httpContext),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Payment creation - very restrictive
            options.AddPolicy("payment", httpContext =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: GetClientIdentifier(httpContext),
                    factory: partition => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 10,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        TokensPerPeriod = 5,
                        AutoReplenishment = true
                    }));

            // Admin endpoints - very restrictive
            options.AddPolicy("admin", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIdentifier(httpContext),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5)
                    }));

            // Sliding window for burst protection
            options.AddPolicy("burst", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: GetClientIdentifier(httpContext),
                    factory: partition => new SlidingWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6 // 10-second segments
                    }));

            // Custom rejection response
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                
                var retryAfter = GetRetryAfterHeader(context);
                if (!string.IsNullOrEmpty(retryAfter))
                {
                    context.HttpContext.Response.Headers.Add("Retry-After", retryAfter);
                }

                await context.HttpContext.Response.WriteAsync(
                    "Rate limit exceeded. Please try again later.", 
                    cancellationToken: token);
            };
        });

        return services;
    }

    private static string GetClientIdentifier(HttpContext httpContext)
    {
        // Try to get client identifier in order of preference:
        // 1. Authenticated user ID
        // 2. API key from headers
        // 3. Client IP address

        var userId = httpContext.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        var apiKey = httpContext.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return $"apikey:{apiKey}";
        }

        // Get client IP address
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Check for forwarded headers (when behind proxy)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            clientIp = forwardedFor.Split(',')[0].Trim();
        }
        
        var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            clientIp = realIp;
        }

        return $"ip:{clientIp}";
    }

    private static string GetRetryAfterHeader(OnRejectedContext context)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            return ((int)retryAfter.TotalSeconds).ToString();
        }

        return "60"; // Default to 60 seconds
    }
}

public class RateLimitingLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingLoggingMiddleware> _logger;

    public RateLimitingLoggingMiddleware(RequestDelegate next, ILogger<RateLimitingLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            await _next(context);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            
            if (context.Response.StatusCode == 429)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for {Method} {Path} from {ClientId} in {Duration}ms",
                    context.Request.Method,
                    context.Request.Path,
                    GetClientId(context),
                    duration.TotalMilliseconds);
            }
            else if (duration.TotalMilliseconds > 1000) // Log slow requests
            {
                _logger.LogWarning(
                    "Slow request: {Method} {Path} from {ClientId} took {Duration}ms",
                    context.Request.Method,
                    context.Request.Path,
                    GetClientId(context),
                    duration.TotalMilliseconds);
            }
        }
    }

    private static string GetClientId(HttpContext context)
    {
        return context.User?.Identity?.Name ?? 
               context.Connection.RemoteIpAddress?.ToString() ?? 
               "unknown";
    }
}