using Microsoft.Extensions.Options;
using System.Text;
using CryptoPayment.API.Services;

namespace CryptoPayment.API.Middleware;

public class WebhookSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WebhookSecurityMiddleware> _logger;
    private readonly IWebhookSecurityService _securityService;
    private readonly WebhookSecurityOptions _options;

    public WebhookSecurityMiddleware(
        RequestDelegate next,
        ILogger<WebhookSecurityMiddleware> logger,
        IWebhookSecurityService securityService,
        IOptions<WebhookSecurityOptions> options)
    {
        _next = next;
        _logger = logger;
        _securityService = securityService;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to webhook endpoints
        if (!IsWebhookEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        try
        {
            // Read the request body
            var requestBody = await ReadRequestBodyAsync(context.Request);
            
            // Create webhook request object
            var webhookRequest = new WebhookRequest
            {
                RequestId = context.Request.Headers["X-Request-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString(),
                Payload = requestBody,
                Signature = GetSignatureFromHeaders(context.Request.Headers),
                IpAddress = GetClientIpAddress(context),
                ProviderId = GetProviderIdFromPath(context.Request.Path),
                Timestamp = context.Request.Headers["X-Timestamp"].FirstOrDefault(),
                PayloadSize = Encoding.UTF8.GetByteCount(requestBody),
                UserAgent = context.Request.Headers.UserAgent.FirstOrDefault()
            };

            // Validate the webhook request
            var validationResult = await _securityService.ValidateWebhookRequestAsync(webhookRequest, context.RequestAborted);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Webhook request validation failed: {Reason}", validationResult.FailureReason);
                
                context.Response.StatusCode = 403; // Forbidden
                await context.Response.WriteAsync(new
                {
                    error = "Webhook validation failed",
                    reason = validationResult.FailureReason
                }.ToString() ?? string.Empty);
                return;
            }

            // Store the validated request in context for downstream processing
            context.Items["ValidatedWebhookRequest"] = webhookRequest;
            context.Items["WebhookValidationResult"] = validationResult;

            // Reset the request body stream for downstream middleware
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in webhook security middleware");
            
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal server error during webhook validation");
        }
    }

    private static bool IsWebhookEndpoint(PathString path)
    {
        return path.StartsWithSegments("/api/webhooks") || 
               path.StartsWithSegments("/webhooks") ||
               path.ToString().Contains("webhook", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.Body == null)
            return string.Empty;

        if (request.ContentLength > _options.MaxPayloadSize)
        {
            throw new InvalidOperationException($"Request body size {request.ContentLength} exceeds maximum allowed {_options.MaxPayloadSize}");
        }

        request.EnableBuffering();
        
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        
        request.Body.Position = 0;
        
        return body;
    }

    private static string GetSignatureFromHeaders(IHeaderDictionary headers)
    {
        // Check common signature header names
        var signatureHeaders = new[]
        {
            "X-Signature",
            "X-Hub-Signature",
            "X-Hub-Signature-256",
            "Signature",
            "Authorization"
        };

        foreach (var headerName in signatureHeaders)
        {
            if (headers.TryGetValue(headerName, out var values))
            {
                var signature = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(signature))
                {
                    return signature;
                }
            }
        }

        return string.Empty;
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for IP in headers (for reverse proxy scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP if multiple are present
            return forwardedFor.Split(',').FirstOrDefault()?.Trim() ?? string.Empty;
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string? GetProviderIdFromPath(PathString path)
    {
        // Extract provider ID from path like /api/webhooks/{providerId}
        var segments = path.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        if (segments.Length >= 3 && segments[^2].Equals("webhooks", StringComparison.OrdinalIgnoreCase))
        {
            return segments[^1];
        }

        return null;
    }
}

public static class WebhookSecurityMiddlewareExtensions
{
    public static IApplicationBuilder UseWebhookSecurity(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<WebhookSecurityMiddleware>();
    }
}