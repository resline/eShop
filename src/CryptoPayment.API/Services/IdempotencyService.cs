using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace eShop.CryptoPayment.API.Services;

public interface IIdempotencyService
{
    Task<IdempotencyResult<T>> ExecuteIdempotentAsync<T>(
        string idempotencyKey, 
        Func<Task<T>> operation,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);
        
    Task<bool> IsIdempotencyKeyUsedAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task InvalidateIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<string> GenerateIdempotencyKeyAsync(object request);
    Task<IdempotencyMetrics> GetMetricsAsync();
}

public class IdempotencyService : IIdempotencyService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyService> _logger;
    private readonly IdempotencyOptions _options;
    
    // In-memory tracking for metrics and frequent access
    private readonly Dictionary<string, DateTime> _recentKeys = new();
    private readonly object _metricsLock = new();
    private IdempotencyMetrics _metrics = new();

    public IdempotencyService(
        IDistributedCache cache,
        ILogger<IdempotencyService> logger,
        IdempotencyOptions options)
    {
        _cache = cache;
        _logger = logger;
        _options = options;
    }

    public async Task<IdempotencyResult<T>> ExecuteIdempotentAsync<T>(
        string idempotencyKey, 
        Func<Task<T>> operation,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(idempotencyKey));
        }

        var keyWithPrefix = GetCacheKey(idempotencyKey);
        var lockKey = GetLockKey(idempotencyKey);
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        // First, check if we already have a result cached
        var existingResult = await GetCachedResultAsync<T>(keyWithPrefix, cancellationToken);
        if (existingResult != null)
        {
            _logger.LogDebug("Idempotency key {Key} found, returning cached result", idempotencyKey);
            IncrementMetric(ref _metrics.CacheHits);
            UpdateRecentKey(idempotencyKey);
            
            return new IdempotencyResult<T>
            {
                Value = existingResult.Value,
                WasCached = true,
                ExecutionTime = TimeSpan.Zero,
                CachedAt = existingResult.CachedAt
            };
        }

        // Use distributed locking to prevent duplicate execution
        var lockResult = await TryAcquireLockAsync(lockKey, effectiveExpiry, cancellationToken);
        if (!lockResult.Success)
        {
            // Another request is processing this key, wait and check again
            _logger.LogDebug("Idempotency key {Key} is being processed by another request, waiting...", idempotencyKey);
            
            await Task.Delay(_options.ConflictRetryDelay, cancellationToken);
            
            var retryResult = await GetCachedResultAsync<T>(keyWithPrefix, cancellationToken);
            if (retryResult != null)
            {
                _logger.LogDebug("Idempotency key {Key} result available after wait", idempotencyKey);
                IncrementMetric(ref _metrics.ConflictResolutions);
                
                return new IdempotencyResult<T>
                {
                    Value = retryResult.Value,
                    WasCached = true,
                    ExecutionTime = TimeSpan.Zero,
                    CachedAt = retryResult.CachedAt
                };
            }
            
            // If still no result, treat as conflict
            IncrementMetric(ref _metrics.Conflicts);
            throw new IdempotencyConflictException($"Idempotency key {idempotencyKey} is being processed concurrently");
        }

        try
        {
            // Execute the operation
            _logger.LogDebug("Executing operation for idempotency key {Key}", idempotencyKey);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var result = await operation();
            stopwatch.Stop();
            
            // Cache the result
            var cachedResult = new CachedIdempotentResult<T>
            {
                Value = result,
                CachedAt = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey
            };
            
            await SetCachedResultAsync(keyWithPrefix, cachedResult, effectiveExpiry, cancellationToken);
            
            _logger.LogInformation("Operation completed for idempotency key {Key} in {Duration}ms", 
                idempotencyKey, stopwatch.ElapsedMilliseconds);
            
            IncrementMetric(ref _metrics.NewExecutions);
            UpdateRecentKey(idempotencyKey);
            
            return new IdempotencyResult<T>
            {
                Value = result,
                WasCached = false,
                ExecutionTime = stopwatch.Elapsed,
                CachedAt = cachedResult.CachedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed for idempotency key {Key}", idempotencyKey);
            IncrementMetric(ref _metrics.Failures);
            throw;
        }
        finally
        {
            // Always release the lock
            await ReleaseLockAsync(lockKey, lockResult.LockValue, cancellationToken);
        }
    }

    public async Task<bool> IsIdempotencyKeyUsedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var keyWithPrefix = GetCacheKey(idempotencyKey);
        var cachedJson = await _cache.GetStringAsync(keyWithPrefix, cancellationToken);
        return !string.IsNullOrEmpty(cachedJson);
    }

    public async Task InvalidateIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var keyWithPrefix = GetCacheKey(idempotencyKey);
        await _cache.RemoveAsync(keyWithPrefix, cancellationToken);
        
        lock (_metricsLock)
        {
            _recentKeys.Remove(idempotencyKey);
        }
        
        _logger.LogDebug("Invalidated idempotency key {Key}", idempotencyKey);
    }

    public async Task<string> GenerateIdempotencyKeyAsync(object request)
    {
        // Generate a deterministic key based on the request content
        var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(requestJson));
        var hashString = Convert.ToHexString(hash);
        
        // Include timestamp to make keys unique per time window if needed
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHH"); // Hour-based grouping
        
        return $"auto_{timestamp}_{hashString}";
    }

    public async Task<IdempotencyMetrics> GetMetricsAsync()
    {
        lock (_metricsLock)
        {
            var metrics = new IdempotencyMetrics
            {
                TotalRequests = _metrics.TotalRequests,
                CacheHits = _metrics.CacheHits,
                NewExecutions = _metrics.NewExecutions,
                Conflicts = _metrics.Conflicts,
                ConflictResolutions = _metrics.ConflictResolutions,
                Failures = _metrics.Failures,
                RecentKeysCount = _recentKeys.Count,
                CacheHitRatio = _metrics.TotalRequests > 0 ? (double)_metrics.CacheHits / _metrics.TotalRequests : 0,
                LastUpdated = DateTime.UtcNow
            };
            
            // Clean up old recent keys
            var cutoff = DateTime.UtcNow.AddMinutes(-30);
            var keysToRemove = _recentKeys.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
            foreach (var key in keysToRemove)
            {
                _recentKeys.Remove(key);
            }
            
            return metrics;
        }
    }

    private async Task<CachedIdempotentResult<T>?> GetCachedResultAsync<T>(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (string.IsNullOrEmpty(cachedJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CachedIdempotentResult<T>>(cachedJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached result for key {CacheKey}", cacheKey);
            return null;
        }
    }

    private async Task SetCachedResultAsync<T>(string cacheKey, CachedIdempotentResult<T> result, TimeSpan expiry, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            };

            await _cache.SetStringAsync(cacheKey, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache result for key {CacheKey}", cacheKey);
            throw;
        }
    }

    private async Task<LockResult> TryAcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken cancellationToken)
    {
        var lockValue = Guid.NewGuid().ToString();
        var lockData = new DistributedLockData
        {
            LockValue = lockValue,
            AcquiredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(expiry)
        };

        try
        {
            var lockJson = JsonSerializer.Serialize(lockData);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            };

            // Try to set the lock only if it doesn't exist
            var existingLock = await _cache.GetStringAsync(lockKey, cancellationToken);
            if (existingLock != null)
            {
                // Lock already exists, check if it's expired
                try
                {
                    var existingLockData = JsonSerializer.Deserialize<DistributedLockData>(existingLock);
                    if (existingLockData?.ExpiresAt > DateTime.UtcNow)
                    {
                        return new LockResult { Success = false };
                    }
                }
                catch
                {
                    // Invalid lock data, treat as expired
                }
            }

            await _cache.SetStringAsync(lockKey, lockJson, options, cancellationToken);
            return new LockResult { Success = true, LockValue = lockValue };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire lock for key {LockKey}", lockKey);
            return new LockResult { Success = false };
        }
    }

    private async Task ReleaseLockAsync(string lockKey, string? lockValue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(lockValue))
        {
            return;
        }

        try
        {
            var existingLock = await _cache.GetStringAsync(lockKey, cancellationToken);
            if (existingLock != null)
            {
                var lockData = JsonSerializer.Deserialize<DistributedLockData>(existingLock);
                if (lockData?.LockValue == lockValue)
                {
                    await _cache.RemoveAsync(lockKey, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release lock for key {LockKey}", lockKey);
        }
    }

    private string GetCacheKey(string idempotencyKey) => $"{_options.CacheKeyPrefix}:result:{idempotencyKey}";
    private string GetLockKey(string idempotencyKey) => $"{_options.CacheKeyPrefix}:lock:{idempotencyKey}";

    private void IncrementMetric(ref long metric)
    {
        lock (_metricsLock)
        {
            Interlocked.Increment(ref metric);
            Interlocked.Increment(ref _metrics.TotalRequests);
        }
    }

    private void UpdateRecentKey(string idempotencyKey)
    {
        lock (_metricsLock)
        {
            _recentKeys[idempotencyKey] = DateTime.UtcNow;
        }
    }
}

// Configuration options
public class IdempotencyOptions
{
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan ConflictRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public string CacheKeyPrefix { get; set; } = "idempotency";
}

// Result types
public class IdempotencyResult<T>
{
    public T Value { get; set; } = default!;
    public bool WasCached { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public DateTime CachedAt { get; set; }
}

public class CachedIdempotentResult<T>
{
    public T Value { get; set; } = default!;
    public DateTime CachedAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
}

public class IdempotencyMetrics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long NewExecutions { get; set; }
    public long Conflicts { get; set; }
    public long ConflictResolutions { get; set; }
    public long Failures { get; set; }
    public int RecentKeysCount { get; set; }
    public double CacheHitRatio { get; set; }
    public DateTime LastUpdated { get; set; }
}

// Locking support
public class LockResult
{
    public bool Success { get; set; }
    public string? LockValue { get; set; }
}

public class DistributedLockData
{
    public string LockValue { get; set; } = "";
    public DateTime AcquiredAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// Custom exceptions
public class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message) : base(message) { }
    public IdempotencyConflictException(string message, Exception innerException) : base(message, innerException) { }
}

// Attribute for automatic idempotency handling
[AttributeUsage(AttributeTargets.Method)]
public class IdempotentAttribute : Attribute
{
    public int ExpiryMinutes { get; set; } = 60;
    public bool GenerateKeyFromParameters { get; set; } = true;
    public string? CustomKeyParameter { get; set; }
}

// Middleware for automatic idempotency handling
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IIdempotencyService idempotencyService,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only handle POST/PUT/PATCH requests
        if (!IsIdempotencyRequired(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            // Generate key from request content if not provided
            if (context.Request.ContentLength > 0)
            {
                context.Request.EnableBuffering();
                var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
                context.Request.Body.Position = 0;
                
                idempotencyKey = await _idempotencyService.GenerateIdempotencyKeyAsync(new { 
                    Path = context.Request.Path,
                    Method = context.Request.Method,
                    Body = requestBody 
                });
            }
        }

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            await _next(context);
            return;
        }

        // Check if this request was already processed
        var isUsed = await _idempotencyService.IsIdempotencyKeyUsedAsync(idempotencyKey);
        if (isUsed)
        {
            context.Response.StatusCode = 409; // Conflict
            await context.Response.WriteAsync("Request already processed");
            return;
        }

        // Add idempotency key to context for controller use
        context.Items["IdempotencyKey"] = idempotencyKey;
        
        await _next(context);
    }

    private static bool IsIdempotencyRequired(string method)
    {
        return method is "POST" or "PUT" or "PATCH";
    }
}