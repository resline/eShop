using System.Security.Claims;
using Microsoft.Extensions.Options;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.BlockchainServices.Security;

public interface ISecurityAuditService
{
    Task LogSecurityEventAsync(SecurityEventType eventType, string eventDescription, 
        object? metadata = null, string? userId = null, CancellationToken cancellationToken = default);
    Task LogKeyOperationAsync(KeyOperationType operationType, string keyId, 
        bool successful = true, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task LogAuthenticationEventAsync(AuthenticationEventType eventType, string userId, 
        string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<SecurityAuditLog>> GetSecurityLogsAsync(DateTime? from = null, DateTime? to = null, 
        SecurityEventType? eventType = null, CancellationToken cancellationToken = default);
}

public class SecurityAuditService : ISecurityAuditService
{
    private readonly ILogger<SecurityAuditService> _logger;
    private readonly SecurityOptions _options;
    private readonly List<SecurityAuditLog> _auditLogs = new();
    private readonly SemaphoreSlim _logLock = new(1, 1);

    public SecurityAuditService(ILogger<SecurityAuditService> logger, IOptions<BlockchainOptions> options)
    {
        _logger = logger;
        _options = options.Value.Security;
    }

    public async Task LogSecurityEventAsync(SecurityEventType eventType, string eventDescription, 
        object? metadata = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        var auditLog = new SecurityAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EventType = eventType,
            Description = eventDescription,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Metadata = metadata?.ToString(),
            IpAddress = GetCurrentIpAddress(),
            UserAgent = GetCurrentUserAgent()
        };

        await _logLock.WaitAsync(cancellationToken);
        try
        {
            _auditLogs.Add(auditLog);
            
            // Keep only recent logs to prevent memory issues
            if (_auditLogs.Count > 10000)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-30);
                _auditLogs.RemoveAll(log => log.Timestamp < cutoffDate);
            }
        }
        finally
        {
            _logLock.Release();
        }

        // Log to structured logging as well
        _logger.LogWarning("Security Event: {EventType} - {Description} - User: {UserId} - Timestamp: {Timestamp}", 
            eventType, eventDescription, userId ?? "System", auditLog.Timestamp);
    }

    public async Task LogKeyOperationAsync(KeyOperationType operationType, string keyId, 
        bool successful = true, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        var description = $"Key operation: {operationType} on key ending in {(keyId.Length > 4 ? keyId[^4..] : "****")}";
        if (!successful && !string.IsNullOrEmpty(errorMessage))
        {
            description += $" - Error: {errorMessage}";
        }

        await LogSecurityEventAsync(
            successful ? SecurityEventType.KeyOperationSuccess : SecurityEventType.KeyOperationFailure,
            description,
            new { OperationType = operationType, KeyIdSuffix = keyId.Length > 4 ? keyId[^4..] : "****", Successful = successful },
            cancellationToken: cancellationToken);
    }

    public async Task LogAuthenticationEventAsync(AuthenticationEventType eventType, string userId, 
        string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
    {
        var description = $"Authentication event: {eventType} for user: {userId}";
        
        await LogSecurityEventAsync(
            eventType switch
            {
                AuthenticationEventType.LoginSuccess => SecurityEventType.AuthenticationSuccess,
                AuthenticationEventType.LoginFailure => SecurityEventType.AuthenticationFailure,
                AuthenticationEventType.Logout => SecurityEventType.UserLogout,
                AuthenticationEventType.PasswordChange => SecurityEventType.PasswordChange,
                AuthenticationEventType.AccountLocked => SecurityEventType.AccountLocked,
                _ => SecurityEventType.Other
            },
            description,
            new { AuthEventType = eventType, IpAddress = ipAddress, UserAgent = userAgent },
            userId,
            cancellationToken);
    }

    public async Task<IEnumerable<SecurityAuditLog>> GetSecurityLogsAsync(DateTime? from = null, DateTime? to = null, 
        SecurityEventType? eventType = null, CancellationToken cancellationToken = default)
    {
        await _logLock.WaitAsync(cancellationToken);
        try
        {
            var query = _auditLogs.AsEnumerable();

            if (from.HasValue)
                query = query.Where(log => log.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(log => log.Timestamp <= to.Value);

            if (eventType.HasValue)
                query = query.Where(log => log.EventType == eventType.Value);

            return query.OrderByDescending(log => log.Timestamp).ToList();
        }
        finally
        {
            _logLock.Release();
        }
    }

    private string? GetCurrentIpAddress()
    {
        // In a real application, you would get this from HttpContext
        // For now, return a placeholder
        return "127.0.0.1";
    }

    private string? GetCurrentUserAgent()
    {
        // In a real application, you would get this from HttpContext
        // For now, return a placeholder
        return "CryptoPayment-Service";
    }
}

public class SecurityAuditLog
{
    public string Id { get; set; } = string.Empty;
    public SecurityEventType EventType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Metadata { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public enum SecurityEventType
{
    KeyOperationSuccess,
    KeyOperationFailure,
    AuthenticationSuccess,
    AuthenticationFailure,
    UserLogout,
    PasswordChange,
    AccountLocked,
    UnauthorizedAccess,
    DataExport,
    ConfigurationChange,
    SecurityPolicyViolation,
    Other
}

public enum KeyOperationType
{
    Generation,
    Encryption,
    Decryption,
    Storage,
    Retrieval,
    Deletion,
    Rotation
}

public enum AuthenticationEventType
{
    LoginSuccess,
    LoginFailure,
    Logout,
    PasswordChange,
    AccountLocked
}

public static class SecurityAuditExtensions
{
    public static IServiceCollection AddSecurityAudit(this IServiceCollection services)
    {
        services.AddSingleton<ISecurityAuditService, SecurityAuditService>();
        return services;
    }
}