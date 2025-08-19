using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Hubs;

[Authorize]
public class PaymentStatusHub : Hub
{
    private readonly ILogger<PaymentStatusHub> _logger;

    public PaymentStatusHub(ILogger<PaymentStatusHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinPaymentGroup(string paymentId)
    {
        if (string.IsNullOrEmpty(paymentId))
        {
            _logger.LogWarning("JoinPaymentGroup called with empty paymentId from connection {ConnectionId}", Context.ConnectionId);
            return;
        }

        var groupName = GetPaymentGroupName(paymentId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} joined payment group {GroupName}", 
            Context.ConnectionId, groupName);
    }

    public async Task LeavePaymentGroup(string paymentId)
    {
        if (string.IsNullOrEmpty(paymentId))
        {
            _logger.LogWarning("LeavePaymentGroup called with empty paymentId from connection {ConnectionId}", Context.ConnectionId);
            return;
        }

        var groupName = GetPaymentGroupName(paymentId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} left payment group {GroupName}", 
            Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}, User: {UserId}", 
            Context.ConnectionId, Context.UserIdentifier);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception, "Client disconnected with error: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    private static string GetPaymentGroupName(string paymentId) => $"payment_{paymentId}";
}

// Extension methods for IHubContext to send notifications
public static class PaymentStatusHubExtensions
{
    public static async Task NotifyPaymentStatusChanged(
        this IHubContext<PaymentStatusHub> hubContext,
        string paymentId,
        PaymentStatusChangedNotification notification)
    {
        var groupName = $"payment_{paymentId}";
        await hubContext.Clients.Group(groupName).SendAsync("PaymentStatusChanged", notification);
    }

    public static async Task NotifyTransactionDetected(
        this IHubContext<PaymentStatusHub> hubContext,
        string paymentId,
        TransactionDetectedNotification notification)
    {
        var groupName = $"payment_{paymentId}";
        await hubContext.Clients.Group(groupName).SendAsync("TransactionDetected", notification);
    }

    public static async Task NotifyPaymentExpired(
        this IHubContext<PaymentStatusHub> hubContext,
        string paymentId,
        PaymentExpiredNotification notification)
    {
        var groupName = $"payment_{paymentId}";
        await hubContext.Clients.Group(groupName).SendAsync("PaymentExpired", notification);
    }

    public static async Task NotifyExchangeRateUpdated(
        this IHubContext<PaymentStatusHub> hubContext,
        string paymentId,
        ExchangeRateUpdatedNotification notification)
    {
        var groupName = $"payment_{paymentId}";
        await hubContext.Clients.Group(groupName).SendAsync("ExchangeRateUpdated", notification);
    }
}

// Notification models for real-time updates
public record PaymentStatusChangedNotification(
    string PaymentId,
    PaymentStatus OldStatus,
    PaymentStatus NewStatus,
    string? TransactionHash = null,
    decimal? ReceivedAmount = null,
    int? Confirmations = null,
    DateTime Timestamp = default
)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}

public record TransactionDetectedNotification(
    string PaymentId,
    string TransactionHash,
    decimal Amount,
    int Confirmations,
    DateTime Timestamp = default
)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}

public record PaymentExpiredNotification(
    string PaymentId,
    DateTime ExpirationTime,
    DateTime Timestamp = default
)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}

public record ExchangeRateUpdatedNotification(
    string PaymentId,
    CryptoCurrencyType Currency,
    decimal NewRate,
    decimal NewCryptoAmount,
    DateTime Timestamp = default
)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}