using Microsoft.AspNetCore.SignalR;
using CryptoPayment.BlockchainServices.Monitoring;
using CryptoPayment.BlockchainServices.Models;
using eShop.CryptoPayment.API.Hubs;
using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

// Enhanced notification service with adaptive batching and priority queuing
public class EnhancedPaymentNotificationService : IPaymentNotificationService
{
    private readonly IHubContext<PaymentStatusHub> _hubContext;
    private readonly TransactionMonitorService _monitorService;
    private readonly ICryptoPaymentService _paymentService;
    private readonly ILogger<EnhancedPaymentNotificationService> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // Adaptive batch processing with priority queues
    private readonly Queue<NotificationBatch> _highPriorityQueue;
    private readonly Queue<NotificationBatch> _normalPriorityQueue;
    private readonly Queue<NotificationBatch> _lowPriorityQueue;
    private readonly Timer _batchTimer;
    private readonly object _batchLock = new();
    
    // Adaptive settings
    private int _currentBatchInterval = 1000; // Start with 1 second
    private const int MinBatchInterval = 100; // Minimum 100ms
    private const int MaxBatchInterval = 5000; // Maximum 5 seconds
    private const int BaseBatchSize = 50;
    private int _adaptiveBatchSize = 50;
    private DateTime _lastProcessTime = DateTime.UtcNow;
    private int _recentNotificationCount = 0;
    private readonly int[] _recentCounts = new int[10]; // Rolling window for load tracking
    private int _recentCountIndex = 0;

    public EnhancedPaymentNotificationService(
        IHubContext<PaymentStatusHub> hubContext,
        TransactionMonitorService monitorService,
        ICryptoPaymentService paymentService,
        ILogger<EnhancedPaymentNotificationService> logger)
    {
        _hubContext = hubContext;
        _monitorService = monitorService;
        _paymentService = paymentService;
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        
        _highPriorityQueue = new Queue<NotificationBatch>();
        _normalPriorityQueue = new Queue<NotificationBatch>();
        _lowPriorityQueue = new Queue<NotificationBatch>();
        _batchTimer = new Timer(ProcessBatchedNotifications, null, _currentBatchInterval, _currentBatchInterval);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Enhanced Payment Notification Service with adaptive batching");
        
        // Subscribe to transaction monitor events
        _monitorService.TransactionUpdated += OnTransactionUpdated;
        _monitorService.TransactionConfirmed += OnTransactionConfirmed;
        _monitorService.TransactionFailed += OnTransactionFailed;
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Enhanced Payment Notification Service");
        
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
        var priority = DeterminePriority(method, notification);
        var batch = new NotificationBatch
        {
            PaymentId = paymentId,
            Method = method,
            Notification = notification,
            Timestamp = DateTime.UtcNow,
            Priority = priority
        };
        
        lock (_batchLock)
        {
            var targetQueue = priority switch
            {
                NotificationPriority.High => _highPriorityQueue,
                NotificationPriority.Normal => _normalPriorityQueue,
                NotificationPriority.Low => _lowPriorityQueue,
                _ => _normalPriorityQueue
            };
            
            targetQueue.Enqueue(batch);
            _recentNotificationCount++;
            
            var totalQueued = GetTotalQueuedCount();
            
            // Adaptive processing: trigger immediately if high load or high priority
            if (totalQueued >= _adaptiveBatchSize || 
                (priority == NotificationPriority.High && totalQueued >= 5))
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
            var totalQueued = GetTotalQueuedCount();
            if (totalQueued == 0) 
            {
                UpdateAdaptiveSettings(0);
                return;
            }
            
            notificationsToProcess = new List<NotificationBatch>();
            
            // Process high priority first, then normal, then low (priority-based processing)
            DequeueFromQueue(_highPriorityQueue, notificationsToProcess, _adaptiveBatchSize / 2);
            DequeueFromQueue(_normalPriorityQueue, notificationsToProcess, _adaptiveBatchSize / 3);
            DequeueFromQueue(_lowPriorityQueue, notificationsToProcess, _adaptiveBatchSize / 6);
            
            UpdateAdaptiveSettings(totalQueued);
        }

        if (notificationsToProcess.Count == 0) return;

        try
        {
            // Group notifications by payment ID for efficient sending
            var groupedNotifications = notificationsToProcess
                .GroupBy(n => n.PaymentId)
                .ToList();

            // Process notifications with rate limiting and retry logic
            var tasks = groupedNotifications.Select(async group =>
            {
                var paymentId = group.Key;
                var groupName = $"payment_{paymentId}";
                
                foreach (var notification in group.OrderByDescending(n => n.Priority))
                {
                    try
                    {
                        await _hubContext.Clients.Group(groupName)
                            .SendAsync(notification.Method, notification.Notification, _cancellationTokenSource.Token);
                            
                        // Add small delay between notifications to prevent overwhelming clients
                        if (group.Count() > 1)
                        {
                            await Task.Delay(10, _cancellationTokenSource.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send {Method} notification for payment {PaymentId} (Priority: {Priority})",
                            notification.Method, paymentId, notification.Priority);
                            
                        // Retry for high priority notifications
                        if (notification.Priority == NotificationPriority.High && notification.RetryCount < 3)
                        {
                            notification.RetryCount++;
                            notification.LastRetryTime = DateTime.UtcNow;
                            
                            lock (_batchLock)
                            {
                                _highPriorityQueue.Enqueue(notification);
                            }
                        }
                    }
                }
            });

            await Task.WhenAll(tasks);
            
            if (notificationsToProcess.Count > 0)
            {
                var priorityBreakdown = notificationsToProcess
                    .GroupBy(n => n.Priority)
                    .ToDictionary(g => g.Key, g => g.Count());
                    
                _logger.LogDebug("Processed {Count} notifications in batch. Priority breakdown: {PriorityBreakdown}. Current batch interval: {Interval}ms", 
                    notificationsToProcess.Count, 
                    string.Join(", ", priorityBreakdown.Select(kvp => $"{kvp.Key}: {kvp.Value}")),
                    _currentBatchInterval);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batched notifications");
        }
    }

    private NotificationPriority DeterminePriority(string method, object notification)
    {
        return method switch
        {
            "PaymentStatusChanged" => NotificationPriority.High,
            "TransactionDetected" => NotificationPriority.High, 
            "PaymentExpired" => NotificationPriority.Normal,
            "ExchangeRateUpdated" => NotificationPriority.Low,
            _ => NotificationPriority.Normal
        };
    }
    
    private int GetTotalQueuedCount()
    {
        return _highPriorityQueue.Count + _normalPriorityQueue.Count + _lowPriorityQueue.Count;
    }
    
    private void DequeueFromQueue(Queue<NotificationBatch> queue, List<NotificationBatch> target, int maxCount)
    {
        var count = 0;
        while (queue.Count > 0 && count < maxCount && target.Count < _adaptiveBatchSize)
        {
            target.Add(queue.Dequeue());
            count++;
        }
    }
    
    private void UpdateAdaptiveSettings(int currentLoad)
    {
        // Update rolling window for load tracking
        _recentCounts[_recentCountIndex] = _recentNotificationCount;
        _recentCountIndex = (_recentCountIndex + 1) % _recentCounts.Length;
        _recentNotificationCount = 0;
        
        // Calculate average load over the rolling window
        var averageLoad = _recentCounts.Average();
        
        // Adapt batch interval and size based on load
        if (averageLoad > 100) // High load - process faster
        {
            _currentBatchInterval = Math.Max(MinBatchInterval, _currentBatchInterval - 100);
            _adaptiveBatchSize = Math.Min(BaseBatchSize * 2, 200);
        }
        else if (averageLoad < 10) // Low load - process slower to save resources
        {
            _currentBatchInterval = Math.Min(MaxBatchInterval, _currentBatchInterval + 200);
            _adaptiveBatchSize = Math.Max(BaseBatchSize / 2, 10);
        }
        else // Normal load - reset to defaults
        {
            _currentBatchInterval = 1000;
            _adaptiveBatchSize = BaseBatchSize;
        }
        
        // Update timer if interval has changed significantly
        var timeSinceLastProcess = DateTime.UtcNow - _lastProcessTime;
        if (timeSinceLastProcess.TotalMilliseconds > _currentBatchInterval * 1.5)
        {
            _batchTimer?.Change(_currentBatchInterval, _currentBatchInterval);
        }
        
        _lastProcessTime = DateTime.UtcNow;
    }

    // Event handlers for transaction monitoring
    private async void OnTransactionUpdated(object? sender, TransactionUpdatedEventArgs e)
    {
        try
        {
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
            var payment = await _paymentService.GetPaymentByTransactionHashAsync(transactionHash);
            return payment?.PaymentId;
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

// Helper classes for batching notifications
public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

internal class NotificationBatch
{
    public string PaymentId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public object Notification { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public int RetryCount { get; set; } = 0;
    public DateTime? LastRetryTime { get; set; }
}

// Metrics for monitoring batch performance
public class NotificationBatchMetrics
{
    public int TotalProcessed { get; set; }
    public int HighPriorityProcessed { get; set; }
    public int NormalPriorityProcessed { get; set; }
    public int LowPriorityProcessed { get; set; }
    public int CurrentBatchInterval { get; set; }
    public int CurrentBatchSize { get; set; }
    public double AverageLoad { get; set; }
    public int QueuedCount { get; set; }
    public DateTime LastProcessTime { get; set; }
}