using Microsoft.AspNetCore.SignalR;
using CryptoPayment.BlockchainServices.Monitoring;
using CryptoPayment.BlockchainServices.Models;
using eShop.CryptoPayment.API.Hubs;
using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

public interface IPaymentNotificationService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task NotifyPaymentStatusChangedAsync(string paymentId, PaymentStatus oldStatus, PaymentStatus newStatus, 
        string? transactionHash = null, decimal? receivedAmount = null, int? confirmations = null);
    Task NotifyTransactionDetectedAsync(string paymentId, string transactionHash, decimal amount, int confirmations);
    Task NotifyPaymentExpiredAsync(string paymentId, DateTime expirationTime);
    Task NotifyExchangeRateUpdatedAsync(string paymentId, CryptoCurrencyType currency, decimal newRate, decimal newCryptoAmount);
}

public class PaymentNotificationService : IPaymentNotificationService
{
    private readonly IHubContext<PaymentStatusHub> _hubContext;
    private readonly TransactionMonitorService _monitorService;
    private readonly ICryptoPaymentService _paymentService;
    private readonly ILogger<PaymentNotificationService> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // Batch processing
    private readonly List<NotificationBatch> _pendingNotifications;
    private readonly Timer _batchTimer;
    private readonly object _batchLock = new();
    private const int BatchIntervalMs = 1000; // Send batched notifications every second
    private const int MaxBatchSize = 50;

    public PaymentNotificationService(
        IHubContext<PaymentStatusHub> hubContext,
        TransactionMonitorService monitorService,
        ICryptoPaymentService paymentService,
        ILogger<PaymentNotificationService> logger)
    {
        _hubContext = hubContext;
        _monitorService = monitorService;
        _paymentService = paymentService;
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        
        _pendingNotifications = new List<NotificationBatch>();
        _batchTimer = new Timer(ProcessBatchedNotifications, null, BatchIntervalMs, BatchIntervalMs);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Payment Notification Service");
        
        // Subscribe to transaction monitor events
        _monitorService.TransactionUpdated += OnTransactionUpdated;
        _monitorService.TransactionConfirmed += OnTransactionConfirmed;
        _monitorService.TransactionFailed += OnTransactionFailed;
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Payment Notification Service");
        
        // Unsubscribe from events
        _monitorService.TransactionUpdated -= OnTransactionUpdated;
        _monitorService.TransactionConfirmed -= OnTransactionConfirmed;
        _monitorService.TransactionFailed -= OnTransactionFailed;
        
        _cancellationTokenSource.Cancel();
        _batchTimer?.Dispose();
        
        return Task.CompletedTask;
    }

    public async Task NotifyPaymentStatusChangedAsync(string paymentId, PaymentStatus oldStatus, PaymentStatus newStatus, 
        string? transactionHash = null, decimal? receivedAmount = null, int? confirmations = null)
    {
        var notification = new PaymentStatusChangedNotification(
            paymentId, oldStatus, newStatus, transactionHash, receivedAmount, confirmations);
        
        await AddToBatch(paymentId, "PaymentStatusChanged", notification);
        
        _logger.LogInformation("Queued payment status change notification for {PaymentId}: {OldStatus} -> {NewStatus}",
            paymentId, oldStatus, newStatus);
    }

    public async Task NotifyTransactionDetectedAsync(string paymentId, string transactionHash, decimal amount, int confirmations)
    {
        var notification = new TransactionDetectedNotification(paymentId, transactionHash, amount, confirmations);
        
        await AddToBatch(paymentId, "TransactionDetected", notification);
        
        _logger.LogInformation("Queued transaction detection notification for {PaymentId}: {Hash}",
            paymentId, transactionHash);
    }

    public async Task NotifyPaymentExpiredAsync(string paymentId, DateTime expirationTime)
    {
        var notification = new PaymentExpiredNotification(paymentId, expirationTime);
        
        await AddToBatch(paymentId, "PaymentExpired", notification);
        
        _logger.LogInformation("Queued payment expiration notification for {PaymentId}", paymentId);
    }

    public async Task NotifyExchangeRateUpdatedAsync(string paymentId, CryptoCurrencyType currency, decimal newRate, decimal newCryptoAmount)
    {
        var notification = new ExchangeRateUpdatedNotification(paymentId, currency, newRate, newCryptoAmount);
        
        await AddToBatch(paymentId, "ExchangeRateUpdated", notification);
        
        _logger.LogDebug("Queued exchange rate update notification for {PaymentId}: {Currency} = ${Rate:F2}",
            paymentId, currency, newRate);
    }

    private async Task AddToBatch(string paymentId, string method, object notification)
    {
        lock (_batchLock)
        {
            _pendingNotifications.Add(new NotificationBatch
            {
                PaymentId = paymentId,
                Method = method,
                Notification = notification,
                Timestamp = DateTime.UtcNow
            });

            // If batch is getting too large, process it immediately
            if (_pendingNotifications.Count >= MaxBatchSize)
            {
                _ = Task.Run(() => ProcessBatchedNotifications(null));
            }
        }
    }

    private async void ProcessBatchedNotifications(object? state)
    {
        List<NotificationBatch> notificationsToProcess;
        
        lock (_batchLock)
        {
            if (_pendingNotifications.Count == 0) return;
            
            notificationsToProcess = new List<NotificationBatch>(_pendingNotifications);
            _pendingNotifications.Clear();
        }

        try
        {
            // Group notifications by payment ID for efficient sending
            var groupedNotifications = notificationsToProcess
                .GroupBy(n => n.PaymentId)
                .ToList();

            var tasks = groupedNotifications.Select(async group =>
            {
                var paymentId = group.Key;
                var groupName = $"payment_{paymentId}";
                
                foreach (var notification in group)
                {
                    try
                    {
                        await _hubContext.Clients.Group(groupName)
                            .SendAsync(notification.Method, notification.Notification, _cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send {Method} notification for payment {PaymentId}",
                            notification.Method, paymentId);
                    }
                }
            });

            await Task.WhenAll(tasks);
            
            if (notificationsToProcess.Count > 0)
            {
                _logger.LogDebug("Processed {Count} notifications in batch", notificationsToProcess.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batched notifications");
        }
    }

    private async void OnTransactionUpdated(object? sender, TransactionUpdatedEventArgs e)
    {
        try
        {
            // Find the corresponding payment
            var paymentId = await FindPaymentIdByTransactionHash(e.Transaction.TransactionHash);
            if (paymentId == null) return;

            var newStatus = MapTransactionStatusToPaymentStatus(e.Transaction.Status);
            var oldStatus = MapTransactionStatusToPaymentStatus(e.PreviousStatus);

            await NotifyPaymentStatusChangedAsync(paymentId, oldStatus, newStatus, 
                e.Transaction.TransactionHash, e.Transaction.Amount, e.Transaction.Confirmations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling transaction updated event for {Hash}",
                e.Transaction.TransactionHash);
        }
    }

    private async void OnTransactionConfirmed(object? sender, TransactionConfirmedEventArgs e)
    {
        try
        {
            var paymentId = await FindPaymentIdByTransactionHash(e.Transaction.TransactionHash);
            if (paymentId == null) return;

            await NotifyPaymentStatusChangedAsync(paymentId, PaymentStatus.Pending, PaymentStatus.Confirmed,
                e.Transaction.TransactionHash, e.Transaction.Amount, e.Transaction.Confirmations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling transaction confirmed event for {Hash}",
                e.Transaction.TransactionHash);
        }
    }

    private async void OnTransactionFailed(object? sender, TransactionFailedEventArgs e)
    {
        try
        {
            var paymentId = await FindPaymentIdByTransactionHash(e.Transaction.TransactionHash);
            if (paymentId == null) return;

            var failedStatus = e.Error?.Contains("expired", StringComparison.OrdinalIgnoreCase) == true
                ? PaymentStatus.Expired
                : PaymentStatus.Failed;

            await NotifyPaymentStatusChangedAsync(paymentId, PaymentStatus.Pending, failedStatus,
                e.Transaction.TransactionHash, null, e.Transaction.Confirmations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling transaction failed event for {Hash}",
                e.Transaction.TransactionHash);
        }
    }

    private async Task<string?> FindPaymentIdByTransactionHash(string transactionHash)
    {
        try
        {
            // This would need to be implemented based on your data access patterns
            // For now, returning null - you'd need to add a method to find payment by transaction hash
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding payment ID for transaction {Hash}", transactionHash);
            return null;
        }
    }

    private static PaymentStatus MapTransactionStatusToPaymentStatus(TransactionStatus transactionStatus)
    {
        return transactionStatus switch
        {
            TransactionStatus.Pending => PaymentStatus.Pending,
            TransactionStatus.Confirmed => PaymentStatus.Confirmed,
            TransactionStatus.Failed => PaymentStatus.Failed,
            TransactionStatus.Expired => PaymentStatus.Expired,
            _ => PaymentStatus.Pending
        };
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
        _batchTimer?.Dispose();
    }
}

// Helper class for batching notifications
internal class NotificationBatch
{
    public string PaymentId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public object Notification { get; set; } = null!;
    public DateTime Timestamp { get; set; }
}