using Microsoft.Extensions.Options;
using CryptoPayment.BlockchainServices.Configuration;
using CryptoPayment.BlockchainServices.Abstractions;

namespace CryptoPayment.BlockchainServices.Security;

public interface IKeyRotationService
{
    Task<string> RotateKeyAsync(string keyId, CancellationToken cancellationToken = default);
    Task<bool> IsKeyRotationDueAsync(string keyId, CancellationToken cancellationToken = default);
    Task ScheduleKeyRotationAsync(string keyId, DateTime rotationDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<KeyRotationSchedule>> GetPendingRotationsAsync(CancellationToken cancellationToken = default);
    Task ForceKeyRotationAsync(string keyId, string reason, CancellationToken cancellationToken = default);
}

public class KeyRotationService : IKeyRotationService
{
    private readonly ILogger<KeyRotationService> _logger;
    private readonly IKeyManager _keyManager;
    private readonly IKeyStorage _keyStorage;
    private readonly ISecurityAuditService _auditService;
    private readonly SecurityOptions _options;
    private readonly List<KeyRotationSchedule> _rotationSchedules = new();
    private readonly SemaphoreSlim _rotationLock = new(1, 1);

    // Default rotation interval: 90 days
    private static readonly TimeSpan DefaultRotationInterval = TimeSpan.FromDays(90);

    public KeyRotationService(
        ILogger<KeyRotationService> logger,
        IKeyManager keyManager,
        IKeyStorage keyStorage,
        ISecurityAuditService auditService,
        IOptions<BlockchainOptions> options)
    {
        _logger = logger;
        _keyManager = keyManager;
        _keyStorage = keyStorage;
        _auditService = auditService;
        _options = options.Value.Security;
    }

    public async Task<string> RotateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await _rotationLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting key rotation for key ending in {KeyIdSuffix}", 
                keyId.Length > 4 ? keyId[^4..] : "****");

            // Generate new key
            var newPrivateKey = await _keyManager.GeneratePrivateKeyAsync(cancellationToken);
            var newKeyId = GenerateKeyId();

            // Encrypt new key
            var encryptedNewKey = await _keyManager.EncryptPrivateKeyAsync(newPrivateKey, cancellationToken);

            // Store new key
            var stored = await _keyStorage.StoreKeyAsync(newKeyId, encryptedNewKey, cancellationToken);
            if (!stored)
            {
                await _auditService.LogKeyOperationAsync(KeyOperationType.Rotation, keyId, false, 
                    "Failed to store new rotated key", cancellationToken);
                throw new InvalidOperationException("Failed to store new rotated key");
            }

            // Archive old key (don't delete immediately for rollback capability)
            await ArchiveOldKeyAsync(keyId, cancellationToken);

            // Update rotation schedule
            await UpdateRotationScheduleAsync(newKeyId, cancellationToken);

            // Log successful rotation
            await _auditService.LogKeyOperationAsync(KeyOperationType.Rotation, keyId, true, 
                cancellationToken: cancellationToken);

            _logger.LogInformation("Key rotation completed successfully. New key ID ending in {NewKeyIdSuffix}", 
                newKeyId.Length > 4 ? newKeyId[^4..] : "****");

            return newKeyId;
        }
        catch (Exception ex)
        {
            await _auditService.LogKeyOperationAsync(KeyOperationType.Rotation, keyId, false, 
                ex.Message, cancellationToken);
            _logger.LogError(ex, "Key rotation failed for key ending in {KeyIdSuffix}", 
                keyId.Length > 4 ? keyId[^4..] : "****");
            throw;
        }
        finally
        {
            _rotationLock.Release();
        }
    }

    public async Task<bool> IsKeyRotationDueAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var schedule = _rotationSchedules.FirstOrDefault(s => s.KeyId == keyId);
        if (schedule == null)
        {
            // If no schedule exists, assume key needs rotation if it's older than default interval
            return DateTime.UtcNow - schedule?.LastRotationDate > DefaultRotationInterval;
        }

        return DateTime.UtcNow >= schedule.NextRotationDate;
    }

    public async Task ScheduleKeyRotationAsync(string keyId, DateTime rotationDate, CancellationToken cancellationToken = default)
    {
        var existingSchedule = _rotationSchedules.FirstOrDefault(s => s.KeyId == keyId);
        if (existingSchedule != null)
        {
            existingSchedule.NextRotationDate = rotationDate;
            existingSchedule.ScheduledBy = "System";
            existingSchedule.ScheduledAt = DateTime.UtcNow;
        }
        else
        {
            _rotationSchedules.Add(new KeyRotationSchedule
            {
                KeyId = keyId,
                NextRotationDate = rotationDate,
                LastRotationDate = DateTime.UtcNow,
                RotationInterval = DefaultRotationInterval,
                ScheduledBy = "System",
                ScheduledAt = DateTime.UtcNow
            });
        }

        _logger.LogInformation("Key rotation scheduled for {RotationDate} for key ending in {KeyIdSuffix}", 
            rotationDate, keyId.Length > 4 ? keyId[^4..] : "****");
    }

    public async Task<IEnumerable<KeyRotationSchedule>> GetPendingRotationsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return _rotationSchedules
            .Where(s => s.NextRotationDate <= now)
            .OrderBy(s => s.NextRotationDate)
            .ToList();
    }

    public async Task ForceKeyRotationAsync(string keyId, string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Force key rotation initiated for key ending in {KeyIdSuffix}. Reason: {Reason}", 
            keyId.Length > 4 ? keyId[^4..] : "****", reason);

        await _auditService.LogSecurityEventAsync(SecurityEventType.KeyOperationSuccess, 
            $"Forced key rotation initiated. Reason: {reason}", 
            new { KeyIdSuffix = keyId.Length > 4 ? keyId[^4..] : "****", Reason = reason }, 
            cancellationToken: cancellationToken);

        await RotateKeyAsync(keyId, cancellationToken);
    }

    private async Task ArchiveOldKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        // In a production system, you might move the key to a separate archive storage
        // For now, we'll just mark it as archived in the metadata
        var archiveKeyId = $"archived_{keyId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        
        var oldKey = await _keyStorage.RetrieveKeyAsync(keyId, cancellationToken);
        if (oldKey != null)
        {
            await _keyStorage.StoreKeyAsync(archiveKeyId, oldKey, cancellationToken);
            
            // Delete the original after successful archive
            await _keyStorage.DeleteKeyAsync(keyId, cancellationToken);
            
            _logger.LogInformation("Archived old key with archive ID ending in {ArchiveKeyIdSuffix}", 
                archiveKeyId.Length > 4 ? archiveKeyId[^4..] : "****");
        }
    }

    private async Task UpdateRotationScheduleAsync(string newKeyId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nextRotation = now.Add(DefaultRotationInterval);

        await ScheduleKeyRotationAsync(newKeyId, nextRotation, cancellationToken);
    }

    private static string GenerateKeyId()
    {
        return $"key_{Guid.NewGuid():N}";
    }
}

public class KeyRotationSchedule
{
    public string KeyId { get; set; } = string.Empty;
    public DateTime NextRotationDate { get; set; }
    public DateTime LastRotationDate { get; set; }
    public TimeSpan RotationInterval { get; set; }
    public string ScheduledBy { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class KeyRotationExtensions
{
    public static IServiceCollection AddKeyRotation(this IServiceCollection services)
    {
        services.AddSingleton<IKeyRotationService, KeyRotationService>();
        return services;
    }
}