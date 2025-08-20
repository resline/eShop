using System.Collections.Concurrent;
using System.Timers;
using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using Nethereum.JsonRpc.WebSocketClient;
using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Configuration;
using CryptoPayment.BlockchainServices.Models;
using Timer = System.Timers.Timer;

namespace CryptoPayment.BlockchainServices.Monitoring;

public class TransactionMonitorService : BackgroundService
{
    private readonly ILogger<TransactionMonitorService> _logger;
    private readonly MonitoringOptions _options;
    private readonly IBlockchainServiceFactory _serviceFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    
    private readonly ConcurrentDictionary<string, MonitoredTransaction> _monitoredTransactions;
    private readonly Timer _pollingTimer;
    private WebSocketClient? _webSocketClient;
    private ClientWebSocket? _customWebSocket;
    private bool _isWebSocketConnected;
    private int _reconnectAttempts;
    private readonly CancellationTokenSource _webSocketCancellationTokenSource;

    public TransactionMonitorService(
        ILogger<TransactionMonitorService> logger,
        IOptions<BlockchainOptions> options,
        IBlockchainServiceFactory serviceFactory,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _options = options.Value.Monitoring;
        _serviceFactory = serviceFactory;
        _scopeFactory = scopeFactory;
        
        _monitoredTransactions = new ConcurrentDictionary<string, MonitoredTransaction>();
        _pollingTimer = new Timer(_options.PollingInterval.TotalMilliseconds);
        _pollingTimer.Elapsed += OnPollingTimerElapsed;
        _pollingTimer.AutoReset = true;
        _webSocketCancellationTokenSource = new CancellationTokenSource();
        
        _logger.LogInformation("Transaction Monitor Service initialized");
    }

    public event EventHandler<TransactionUpdatedEventArgs>? TransactionUpdated;
    public event EventHandler<TransactionConfirmedEventArgs>? TransactionConfirmed;
    public event EventHandler<TransactionFailedEventArgs>? TransactionFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Transaction Monitor Service");

        if (_options.EnableWebSocketMonitoring)
        {
            _ = Task.Run(() => StartWebSocketMonitoring(stoppingToken), stoppingToken);
        }

        if (_options.EnablePollingFallback)
        {
            _pollingTimer.Start();
            _logger.LogInformation("Started polling monitoring with interval {Interval}", _options.PollingInterval);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupExpiredTransactions(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Transaction Monitor Service");
        
        _pollingTimer.Stop();
        
        _webSocketCancellationTokenSource.Cancel();
        
        if (_webSocketClient != null)
        {
            await _webSocketClient.StopAsync();
            _webSocketClient.Dispose();
        }
        
        if (_customWebSocket != null)
        {
            await _customWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", cancellationToken);
            _customWebSocket.Dispose();
        }

        await base.StopAsync(cancellationToken);
    }

    public void AddTransaction(string transactionHash, string currency, string address, decimal amount, int requiredConfirmations = 0)
    {
        var monitoredTransaction = new MonitoredTransaction
        {
            TransactionHash = transactionHash,
            Currency = currency,
            Address = address,
            Amount = amount,
            RequiredConfirmations = requiredConfirmations > 0 ? requiredConfirmations : GetDefaultConfirmations(currency),
            AddedAt = DateTime.UtcNow,
            LastChecked = DateTime.UtcNow,
            Status = TransactionStatus.Pending
        };

        _monitoredTransactions.TryAdd(transactionHash, monitoredTransaction);
        _logger.LogInformation("Added transaction {Hash} for monitoring", transactionHash);
    }

    public void RemoveTransaction(string transactionHash)
    {
        _monitoredTransactions.TryRemove(transactionHash, out _);
        _logger.LogInformation("Removed transaction {Hash} from monitoring", transactionHash);
    }

    public MonitoredTransaction? GetTransaction(string transactionHash)
    {
        _monitoredTransactions.TryGetValue(transactionHash, out var transaction);
        return transaction;
    }

    public IEnumerable<MonitoredTransaction> GetAllTransactions()
    {
        return _monitoredTransactions.Values.ToList();
    }

    private async Task StartWebSocketMonitoring(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectWebSocket(cancellationToken);
                
                while (_isWebSocketConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket monitoring error");
                _isWebSocketConnected = false;
                _reconnectAttempts++;

                if (_reconnectAttempts >= _options.MaxReconnectAttempts)
                {
                    _logger.LogError("Max reconnection attempts reached. Disabling WebSocket monitoring.");
                    break;
                }

                await Task.Delay(_options.WebSocketReconnectDelay, cancellationToken);
            }
        }
    }

    private async Task ConnectWebSocket(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<BlockchainOptions>>().Value;
            
            // Try Nethereum WebSocket client first for Ethereum
            if (options.Ethereum.RpcEndpoint.Contains("ethereum") || options.Ethereum.RpcEndpoint.Contains("eth"))
            {
                await ConnectEthereumWebSocket(options.Ethereum.RpcEndpoint, cancellationToken);
            }
            else
            {
                // Use custom WebSocket for other blockchain providers
                await ConnectCustomWebSocket(options.Ethereum.RpcEndpoint, cancellationToken);
            }
            
            _isWebSocketConnected = true;
            _reconnectAttempts = 0;
            
            _logger.LogInformation("WebSocket connected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect WebSocket");
            throw;
        }
    }

    private async void OnPollingTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_monitoredTransactions.IsEmpty) return;

        _logger.LogDebug("Starting polling check for {Count} transactions", _monitoredTransactions.Count);

        var tasks = _monitoredTransactions.Values
            .Where(t => t.Status != TransactionStatus.Confirmed && t.Status != TransactionStatus.Failed)
            .Select(CheckTransactionStatus);

        await Task.WhenAll(tasks);
    }

    private async Task CheckTransactionStatus(MonitoredTransaction monitoredTransaction)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var serviceFactory = scope.ServiceProvider.GetRequiredService<IBlockchainServiceFactory>();
            var service = serviceFactory.GetService(monitoredTransaction.Currency);

            var transaction = await service.GetTransactionAsync(monitoredTransaction.TransactionHash);
            if (transaction == null)
            {
                _logger.LogWarning("Transaction {Hash} not found", monitoredTransaction.TransactionHash);
                return;
            }

            var oldStatus = monitoredTransaction.Status;
            monitoredTransaction.Status = transaction.Status;
            monitoredTransaction.Confirmations = transaction.Confirmations;
            monitoredTransaction.LastChecked = DateTime.UtcNow;

            if (oldStatus != transaction.Status)
            {
                _logger.LogInformation("Transaction {Hash} status changed from {OldStatus} to {NewStatus}",
                    monitoredTransaction.TransactionHash, oldStatus, transaction.Status);

                OnTransactionUpdated(new TransactionUpdatedEventArgs
                {
                    Transaction = monitoredTransaction,
                    PreviousStatus = oldStatus,
                    BlockchainTransaction = transaction
                });
            }

            if (transaction.Status == TransactionStatus.Confirmed)
            {
                OnTransactionConfirmed(new TransactionConfirmedEventArgs
                {
                    Transaction = monitoredTransaction,
                    BlockchainTransaction = transaction
                });
            }
            else if (transaction.Status == TransactionStatus.Failed)
            {
                OnTransactionFailed(new TransactionFailedEventArgs
                {
                    Transaction = monitoredTransaction,
                    BlockchainTransaction = transaction,
                    Error = transaction.Error
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking transaction status for {Hash}", monitoredTransaction.TransactionHash);
        }
    }

    private async Task CleanupExpiredTransactions(CancellationToken cancellationToken)
    {
        var expiredTransactions = _monitoredTransactions.Values
            .Where(t => DateTime.UtcNow - t.AddedAt > _options.TransactionExpiryTime)
            .ToList();

        foreach (var transaction in expiredTransactions)
        {
            if (transaction.Status != TransactionStatus.Confirmed)
            {
                transaction.Status = TransactionStatus.Expired;
                OnTransactionFailed(new TransactionFailedEventArgs
                {
                    Transaction = transaction,
                    Error = "Transaction monitoring expired"
                });
            }

            _monitoredTransactions.TryRemove(transaction.TransactionHash, out _);
            _logger.LogInformation("Cleaned up expired transaction {Hash}", transaction.TransactionHash);
        }
    }

    private int GetDefaultConfirmations(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "BTC" => 3,
            "ETH" or "USDT" or "USDC" => 12,
            _ => 6
        };
    }

    protected virtual void OnTransactionUpdated(TransactionUpdatedEventArgs e)
    {
        TransactionUpdated?.Invoke(this, e);
    }

    protected virtual void OnTransactionConfirmed(TransactionConfirmedEventArgs e)
    {
        TransactionConfirmed?.Invoke(this, e);
    }

    protected virtual void OnTransactionFailed(TransactionFailedEventArgs e)
    {
        TransactionFailed?.Invoke(this, e);
    }
}

public class MonitoredTransaction
{
    public string TransactionHash { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int RequiredConfirmations { get; set; }
    public int Confirmations { get; set; }
    public TransactionStatus Status { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime LastChecked { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class TransactionUpdatedEventArgs : EventArgs
{
    public MonitoredTransaction Transaction { get; set; } = null!;
    public TransactionStatus PreviousStatus { get; set; }
    public Transaction? BlockchainTransaction { get; set; }
}

public class TransactionConfirmedEventArgs : EventArgs
{
    public MonitoredTransaction Transaction { get; set; } = null!;
    public Transaction? BlockchainTransaction { get; set; }
}

public class TransactionFailedEventArgs : EventArgs
{
    public MonitoredTransaction Transaction { get; set; } = null!;
    public Transaction? BlockchainTransaction { get; set; }
    public string? Error { get; set; }
}