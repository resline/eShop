using CryptoPayment.BlockchainServices.Security;

namespace CryptoPayment.API.Services;

public class SecurityMaintenanceService : BackgroundService
{
    private readonly ILogger<SecurityMaintenanceService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour

    public SecurityMaintenanceService(
        ILogger<SecurityMaintenanceService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Security maintenance service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformMaintenanceTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during security maintenance tasks");
            }

            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Security maintenance service stopped");
    }

    private async Task PerformMaintenanceTasksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        
        // Clean up expired webhook requests
        await CleanupWebhookRequestsAsync(scope, cancellationToken);
        
        // Check for keys that need rotation
        await CheckKeyRotationAsync(scope, cancellationToken);
        
        // Perform garbage collection for security cleanup
        await PerformSecurityGarbageCollectionAsync(scope, cancellationToken);

        _logger.LogDebug("Security maintenance tasks completed");
    }

    private async Task CleanupWebhookRequestsAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        try
        {
            var webhookSecurityService = scope.ServiceProvider.GetService<IWebhookSecurityService>();
            if (webhookSecurityService != null)
            {
                await webhookSecurityService.CleanupExpiredRequestsAsync(cancellationToken);
                _logger.LogDebug("Webhook request cleanup completed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup webhook requests");
        }
    }

    private async Task CheckKeyRotationAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        try
        {
            var keyRotationService = scope.ServiceProvider.GetService<IKeyRotationService>();
            if (keyRotationService != null)
            {
                var pendingRotations = await keyRotationService.GetPendingRotationsAsync(cancellationToken);
                
                foreach (var rotation in pendingRotations)
                {
                    try
                    {
                        _logger.LogInformation("Performing scheduled key rotation for key ending in {KeyIdSuffix}", 
                            rotation.KeyId.Length > 4 ? rotation.KeyId[^4..] : "****");
                        
                        await keyRotationService.RotateKeyAsync(rotation.KeyId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to rotate key ending in {KeyIdSuffix}", 
                            rotation.KeyId.Length > 4 ? rotation.KeyId[^4..] : "****");
                    }
                }

                if (pendingRotations.Any())
                {
                    _logger.LogInformation("Processed {Count} key rotations", pendingRotations.Count());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check key rotation schedules");
        }
    }

    private async Task PerformSecurityGarbageCollectionAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        try
        {
            var disposalService = scope.ServiceProvider.GetService<ISecureDisposalService>();
            if (disposalService != null)
            {
                disposalService.ScheduleGarbageCollection();
                _logger.LogDebug("Security garbage collection completed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to perform security garbage collection");
        }
    }
}

public static class SecurityMaintenanceExtensions
{
    public static IServiceCollection AddSecurityMaintenance(this IServiceCollection services)
    {
        services.AddHostedService<SecurityMaintenanceService>();
        return services;
    }
}