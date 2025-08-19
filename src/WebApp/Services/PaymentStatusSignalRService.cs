using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

namespace eShop.WebApp.Services;

public interface IPaymentStatusSignalRService
{
    Task StartAsync();
    Task StopAsync();
    Task JoinPaymentGroupAsync(string paymentId);
    Task LeavePaymentGroupAsync(string paymentId);
    
    event EventHandler<PaymentStatusChangedEventArgs>? PaymentStatusChanged;
    event EventHandler<TransactionDetectedEventArgs>? TransactionDetected;
    event EventHandler<PaymentExpiredEventArgs>? PaymentExpired;
    event EventHandler<ExchangeRateUpdatedEventArgs>? ExchangeRateUpdated;
}

public class PaymentStatusSignalRService : IPaymentStatusSignalRService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentStatusSignalRService> _logger;
    private HubConnection? _connection;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public PaymentStatusSignalRService(
        IConfiguration configuration,
        ILogger<PaymentStatusSignalRService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public event EventHandler<PaymentStatusChangedEventArgs>? PaymentStatusChanged;
    public event EventHandler<TransactionDetectedEventArgs>? TransactionDetected;
    public event EventHandler<PaymentExpiredEventArgs>? PaymentExpired;
    public event EventHandler<ExchangeRateUpdatedEventArgs>? ExchangeRateUpdated;

    public async Task StartAsync()
    {
        try
        {
            var cryptoPaymentApiUrl = _configuration.GetConnectionString("cryptopaymentapi") 
                ?? "https://localhost:7045"; // fallback
            
            var hubUrl = $"{cryptoPaymentApiUrl.TrimEnd('/')}/hubs/payment-status";
            
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    // Configure authentication if needed
                    options.AccessTokenProvider = GetAccessTokenAsync;
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            // Subscribe to hub events
            _connection.On<PaymentStatusChangedNotification>("PaymentStatusChanged", OnPaymentStatusChanged);
            _connection.On<TransactionDetectedNotification>("TransactionDetected", OnTransactionDetected);
            _connection.On<PaymentExpiredNotification>("PaymentExpired", OnPaymentExpired);
            _connection.On<ExchangeRateUpdatedNotification>("ExchangeRateUpdated", OnExchangeRateUpdated);

            // Handle connection events
            _connection.Reconnecting += (error) =>
            {
                _logger.LogWarning("SignalR connection lost. Attempting to reconnect...");
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected with connection ID: {ConnectionId}", connectionId);
                return Task.CompletedTask;
            };

            _connection.Closed += async (error) =>
            {
                if (error != null)
                {
                    _logger.LogError(error, "SignalR connection closed with error");
                }
                else
                {
                    _logger.LogInformation("SignalR connection closed");
                }

                // Attempt to restart if not manually stopped
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(5000, _cancellationTokenSource.Token);
                    try
                    {
                        await _connection.StartAsync(_cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to restart SignalR connection");
                    }
                }
            };

            await _connection.StartAsync(_cancellationTokenSource.Token);
            _logger.LogInformation("SignalR connection started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start SignalR connection");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_connection != null)
        {
            _cancellationTokenSource.Cancel();
            await _connection.StopAsync();
            _logger.LogInformation("SignalR connection stopped");
        }
    }

    public async Task JoinPaymentGroupAsync(string paymentId)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("JoinPaymentGroup", paymentId, _cancellationTokenSource.Token);
            _logger.LogDebug("Joined payment group for {PaymentId}", paymentId);
        }
        else
        {
            _logger.LogWarning("Cannot join payment group - SignalR connection not established");
        }
    }

    public async Task LeavePaymentGroupAsync(string paymentId)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("LeavePaymentGroup", paymentId, _cancellationTokenSource.Token);
            _logger.LogDebug("Left payment group for {PaymentId}", paymentId);
        }
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        // TODO: Implement authentication token retrieval
        // This would typically get the bearer token from the current user session
        return await Task.FromResult<string?>(null);
    }

    private void OnPaymentStatusChanged(PaymentStatusChangedNotification notification)
    {
        _logger.LogInformation("Payment status changed: {PaymentId} from {OldStatus} to {NewStatus}",
            notification.PaymentId, notification.OldStatus, notification.NewStatus);

        PaymentStatusChanged?.Invoke(this, new PaymentStatusChangedEventArgs
        {
            PaymentId = notification.PaymentId,
            OldStatus = notification.OldStatus,
            NewStatus = notification.NewStatus,
            TransactionHash = notification.TransactionHash,
            ReceivedAmount = notification.ReceivedAmount,
            Confirmations = notification.Confirmations,
            Timestamp = notification.Timestamp
        });
    }

    private void OnTransactionDetected(TransactionDetectedNotification notification)
    {
        _logger.LogInformation("Transaction detected: {PaymentId}, Hash: {Hash}",
            notification.PaymentId, notification.TransactionHash);

        TransactionDetected?.Invoke(this, new TransactionDetectedEventArgs
        {
            PaymentId = notification.PaymentId,
            TransactionHash = notification.TransactionHash,
            Amount = notification.Amount,
            Confirmations = notification.Confirmations,
            Timestamp = notification.Timestamp
        });
    }

    private void OnPaymentExpired(PaymentExpiredNotification notification)
    {
        _logger.LogInformation("Payment expired: {PaymentId}", notification.PaymentId);

        PaymentExpired?.Invoke(this, new PaymentExpiredEventArgs
        {
            PaymentId = notification.PaymentId,
            ExpirationTime = notification.ExpirationTime,
            Timestamp = notification.Timestamp
        });
    }

    private void OnExchangeRateUpdated(ExchangeRateUpdatedNotification notification)
    {
        _logger.LogDebug("Exchange rate updated: {PaymentId}, {Currency} = ${Rate:F2}",
            notification.PaymentId, notification.Currency, notification.NewRate);

        ExchangeRateUpdated?.Invoke(this, new ExchangeRateUpdatedEventArgs
        {
            PaymentId = notification.PaymentId,
            Currency = notification.Currency,
            NewRate = notification.NewRate,
            NewCryptoAmount = notification.NewCryptoAmount,
            Timestamp = notification.Timestamp
        });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _connection?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}

// Event argument classes
public class PaymentStatusChangedEventArgs : EventArgs
{
    public string PaymentId { get; set; } = string.Empty;
    public PaymentStatus OldStatus { get; set; }
    public PaymentStatus NewStatus { get; set; }
    public string? TransactionHash { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public int? Confirmations { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TransactionDetectedEventArgs : EventArgs
{
    public string PaymentId { get; set; } = string.Empty;
    public string TransactionHash { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Confirmations { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PaymentExpiredEventArgs : EventArgs
{
    public string PaymentId { get; set; } = string.Empty;
    public DateTime ExpirationTime { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ExchangeRateUpdatedEventArgs : EventArgs
{
    public string PaymentId { get; set; } = string.Empty;
    public CryptoCurrencyType Currency { get; set; }
    public decimal NewRate { get; set; }
    public decimal NewCryptoAmount { get; set; }
    public DateTime Timestamp { get; set; }
}

// Notification models (mirror from CryptoPayment.API)
public record PaymentStatusChangedNotification(
    string PaymentId,
    PaymentStatus OldStatus,
    PaymentStatus NewStatus,
    string? TransactionHash = null,
    decimal? ReceivedAmount = null,
    int? Confirmations = null,
    DateTime Timestamp = default
);

public record TransactionDetectedNotification(
    string PaymentId,
    string TransactionHash,
    decimal Amount,
    int Confirmations,
    DateTime Timestamp = default
);

public record PaymentExpiredNotification(
    string PaymentId,
    DateTime ExpirationTime,
    DateTime Timestamp = default
);

public record ExchangeRateUpdatedNotification(
    string PaymentId,
    CryptoCurrencyType Currency,
    decimal NewRate,
    decimal NewCryptoAmount,
    DateTime Timestamp = default
);

// Enums (mirror from CryptoPayment.API)
public enum PaymentStatus
{
    Pending = 0,
    Confirmed = 1,
    Paid = 2,
    Failed = 3,
    Expired = 4
}

public enum CryptoCurrencyType
{
    Bitcoin = 1,
    Ethereum = 2,
    USDT = 3,
    USDC = 4
}