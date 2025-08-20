using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CryptoPayment.API.Services;

public interface IWebhookSecurityService
{
    Task<WebhookValidationResult> ValidateWebhookRequestAsync(WebhookRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsIpWhitelistedAsync(string ipAddress, string? providerId = null, CancellationToken cancellationToken = default);
    Task<bool> IsRequestDeduplicatedAsync(string requestId, string signature, CancellationToken cancellationToken = default);
    Task AddToWhitelistAsync(string ipAddress, string providerId, CancellationToken cancellationToken = default);
    Task RemoveFromWhitelistAsync(string ipAddress, string providerId, CancellationToken cancellationToken = default);
    bool ValidateRequestSize(int contentLength);
    Task CleanupExpiredRequestsAsync(CancellationToken cancellationToken = default);
}

public class WebhookSecurityService : IWebhookSecurityService
{
    private readonly ILogger<WebhookSecurityService> _logger;
    private readonly IWebhookValidationService _validationService;
    private readonly WebhookSecurityOptions _options;
    private readonly ConcurrentDictionary<string, HashSet<string>> _ipWhitelist = new();
    private readonly ConcurrentDictionary<string, WebhookRequestRecord> _requestCache = new();
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);

    // Default limits
    private const int DefaultMaxPayloadSize = 1024 * 1024; // 1MB
    private const int DefaultRequestCacheSize = 10000;
    private static readonly TimeSpan DefaultRequestCacheExpiry = TimeSpan.FromHours(24);

    public WebhookSecurityService(
        ILogger<WebhookSecurityService> logger,
        IWebhookValidationService validationService,
        IOptions<WebhookSecurityOptions> options)
    {
        _logger = logger;
        _validationService = validationService;
        _options = options.Value;
        
        InitializeDefaultWhitelists();
    }

    public async Task<WebhookValidationResult> ValidateWebhookRequestAsync(WebhookRequest request, CancellationToken cancellationToken = default)
    {
        var result = new WebhookValidationResult();

        try
        {
            // 1. Validate request size
            if (!ValidateRequestSize(request.PayloadSize))
            {
                result.IsValid = false;
                result.FailureReason = $"Request payload size {request.PayloadSize} exceeds maximum allowed {_options.MaxPayloadSize}";
                _logger.LogWarning("Webhook request rejected: payload too large ({PayloadSize} bytes)", request.PayloadSize);
                return result;
            }

            // 2. Validate IP whitelist
            if (!await IsIpWhitelistedAsync(request.IpAddress, request.ProviderId, cancellationToken))
            {
                result.IsValid = false;
                result.FailureReason = $"IP address {request.IpAddress} is not whitelisted for provider {request.ProviderId}";
                _logger.LogWarning("Webhook request rejected: IP {IpAddress} not whitelisted for provider {ProviderId}", 
                    request.IpAddress, request.ProviderId);
                return result;
            }

            // 3. Check for request deduplication
            if (await IsRequestDeduplicatedAsync(request.RequestId, request.Signature, cancellationToken))
            {
                result.IsValid = false;
                result.FailureReason = "Duplicate request detected";
                _logger.LogWarning("Webhook request rejected: duplicate request {RequestId}", request.RequestId);
                return result;
            }

            // 4. Validate payload format
            if (!_validationService.IsValidPayload(request.Payload))
            {
                result.IsValid = false;
                result.FailureReason = "Invalid payload format";
                _logger.LogWarning("Webhook request rejected: invalid payload format");
                return result;
            }

            // 5. Validate signature
            if (!await _validationService.ValidateSignatureAsync(request.Payload, request.Signature, request.ProviderId, cancellationToken))
            {
                result.IsValid = false;
                result.FailureReason = "Invalid signature";
                _logger.LogWarning("Webhook request rejected: invalid signature for provider {ProviderId}", request.ProviderId);
                return result;
            }

            // 6. Validate timestamp (if provided)
            if (!string.IsNullOrEmpty(request.Timestamp))
            {
                if (!await _validationService.ValidateTimestampAsync(request.Timestamp, TimeSpan.FromMinutes(_options.TimestampToleranceMinutes), cancellationToken))
                {
                    result.IsValid = false;
                    result.FailureReason = "Request timestamp outside acceptable tolerance";
                    _logger.LogWarning("Webhook request rejected: timestamp outside tolerance");
                    return result;
                }
            }

            // All validations passed
            result.IsValid = true;
            
            // Cache the request for deduplication
            await CacheRequestAsync(request, cancellationToken);
            
            _logger.LogInformation("Webhook request validated successfully for provider {ProviderId}", request.ProviderId);
            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.FailureReason = $"Validation error: {ex.Message}";
            _logger.LogError(ex, "Error validating webhook request for provider {ProviderId}", request.ProviderId);
            return result;
        }
    }

    public async Task<bool> IsIpWhitelistedAsync(string ipAddress, string? providerId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return false;

        var provider = providerId ?? "default";
        
        // Check if IP is in whitelist for this provider
        if (_ipWhitelist.TryGetValue(provider, out var whitelist))
        {
            return whitelist.Contains(ipAddress) || whitelist.Contains("*"); // * allows all IPs
        }

        // Check global whitelist
        if (_ipWhitelist.TryGetValue("global", out var globalWhitelist))
        {
            return globalWhitelist.Contains(ipAddress) || globalWhitelist.Contains("*");
        }

        // If no whitelist configured, allow all (not recommended for production)
        if (!_options.EnableIpWhitelisting)
        {
            return true;
        }

        return false;
    }

    public async Task<bool> IsRequestDeduplicatedAsync(string requestId, string signature, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(signature))
            return false;

        var cacheKey = GenerateRequestCacheKey(requestId, signature);
        return _requestCache.ContainsKey(cacheKey);
    }

    public async Task AddToWhitelistAsync(string ipAddress, string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ipAddress) || string.IsNullOrEmpty(providerId))
            return;

        // Validate IP address format
        if (!IPAddress.TryParse(ipAddress, out _) && ipAddress != "*")
        {
            throw new ArgumentException($"Invalid IP address format: {ipAddress}");
        }

        _ipWhitelist.AddOrUpdate(providerId, 
            new HashSet<string> { ipAddress },
            (key, existing) => 
            {
                existing.Add(ipAddress);
                return existing;
            });

        _logger.LogInformation("Added IP {IpAddress} to whitelist for provider {ProviderId}", ipAddress, providerId);
    }

    public async Task RemoveFromWhitelistAsync(string ipAddress, string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ipAddress) || string.IsNullOrEmpty(providerId))
            return;

        if (_ipWhitelist.TryGetValue(providerId, out var whitelist))
        {
            whitelist.Remove(ipAddress);
            _logger.LogInformation("Removed IP {IpAddress} from whitelist for provider {ProviderId}", ipAddress, providerId);
        }
    }

    public bool ValidateRequestSize(int contentLength)
    {
        var maxSize = _options.MaxPayloadSize > 0 ? _options.MaxPayloadSize : DefaultMaxPayloadSize;
        return contentLength <= maxSize && contentLength > 0;
    }

    public async Task CleanupExpiredRequestsAsync(CancellationToken cancellationToken = default)
    {
        await _cleanupLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _requestCache
                .Where(kvp => now - kvp.Value.Timestamp > DefaultRequestCacheExpiry)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _requestCache.TryRemove(key, out _);
            }

            // Also limit cache size
            if (_requestCache.Count > DefaultRequestCacheSize)
            {
                var oldestEntries = _requestCache
                    .OrderBy(kvp => kvp.Value.Timestamp)
                    .Take(_requestCache.Count - DefaultRequestCacheSize)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldestEntries)
                {
                    _requestCache.TryRemove(key, out _);
                }
            }

            _logger.LogDebug("Cleaned up {ExpiredCount} expired webhook requests", expiredKeys.Count);
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    private async Task CacheRequestAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = GenerateRequestCacheKey(request.RequestId, request.Signature);
        var record = new WebhookRequestRecord
        {
            RequestId = request.RequestId,
            Signature = request.Signature,
            Timestamp = DateTime.UtcNow,
            ProviderId = request.ProviderId
        };

        _requestCache.TryAdd(cacheKey, record);
    }

    private void InitializeDefaultWhitelists()
    {
        // Add known provider IP ranges (these should be configurable)
        var defaultWhitelists = new Dictionary<string, string[]>
        {
            ["coinbase"] = new[] { "34.96.184.178", "34.96.184.179", "34.96.184.180" },
            ["bitpay"] = new[] { "52.1.222.146", "52.1.222.147" },
            ["global"] = new[] { "127.0.0.1", "::1" } // localhost for development
        };

        foreach (var kvp in defaultWhitelists)
        {
            _ipWhitelist[kvp.Key] = new HashSet<string>(kvp.Value);
        }
    }

    private static string GenerateRequestCacheKey(string requestId, string signature)
    {
        using var sha256 = SHA256.Create();
        var input = $"{requestId}:{signature}";
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }
}

public class WebhookRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
    public string? Timestamp { get; set; }
    public int PayloadSize { get; set; }
    public string? UserAgent { get; set; }
}

public class WebhookValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class WebhookRequestRecord
{
    public string RequestId { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ProviderId { get; set; } = string.Empty;
}

public class WebhookSecurityOptions
{
    public int MaxPayloadSize { get; set; } = 1024 * 1024; // 1MB
    public bool EnableIpWhitelisting { get; set; } = true;
    public int TimestampToleranceMinutes { get; set; } = 5;
    public int RequestCacheExpiryHours { get; set; } = 24;
    public int MaxRequestCacheSize { get; set; } = 10000;
}

public static class WebhookSecurityExtensions
{
    public static IServiceCollection AddWebhookSecurity(this IServiceCollection services, Action<WebhookSecurityOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<WebhookSecurityOptions>(options => { });
        }

        services.AddSingleton<IWebhookSecurityService, WebhookSecurityService>();
        return services;
    }
}