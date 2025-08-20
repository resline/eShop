using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;
using eShop.CryptoPayment.API.Models;
using eShop.CryptoPayment.API.Services;

namespace eShop.CryptoPayment.API.Hubs;

[Authorize]
public class PaymentStatusHub : Hub
{
    private readonly ILogger<PaymentStatusHub> _logger;
    private readonly ICryptoPaymentService _paymentService;
    private readonly IConnectionTracker _connectionTracker;
    private const int MaxConnectionsPerUser = 5;

    public PaymentStatusHub(
        ILogger<PaymentStatusHub> logger, 
        ICryptoPaymentService paymentService,
        IConnectionTracker connectionTracker)
    {
        _logger = logger;
        _paymentService = paymentService;
        _connectionTracker = connectionTracker;
    }

    public async Task JoinPaymentGroup(string paymentId)
    {
        if (string.IsNullOrEmpty(paymentId))
        {
            _logger.LogWarning("JoinPaymentGroup called with empty paymentId from connection {ConnectionId}", Context.ConnectionId);
            return;
        }

        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Unauthorized attempt to join payment group {PaymentId} from connection {ConnectionId}", 
                paymentId, Context.ConnectionId);
            return;
        }

        // Check if user can access this payment
        var canAccess = await _paymentService.CanUserAccessPaymentAsync(paymentId, userId);
        if (!canAccess)
        {
            _logger.LogWarning("User {UserId} attempted to join payment group {PaymentId} without permission", 
                userId, paymentId);
            return;
        }

        // Check connection limit per user
        if (!CanUserAddConnection(userId))
        {
            _logger.LogWarning("User {UserId} exceeded maximum connections limit", userId);
            await Clients.Caller.SendAsync("Error", "Maximum connections limit exceeded");
            return;
        }

        var groupName = GetPaymentGroupName(paymentId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} (User: {UserId}) joined payment group {GroupName}", 
            Context.ConnectionId, userId, groupName);
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
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            AddUserConnection(userId, Context.ConnectionId);
        }
        
        _logger.LogInformation("Client connected: {ConnectionId}, User: {UserId}", 
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            RemoveUserConnection(userId, Context.ConnectionId);
        }
        
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

    private static bool CanUserAddConnection(string userId)
    {
        lock (_connectionLock)
        {
            return !_userConnections.ContainsKey(userId) || 
                   _userConnections[userId].Count < MaxConnectionsPerUser;
        }
    }

    private static void AddUserConnection(string userId, string connectionId)
    {
        lock (_connectionLock)
        {
            if (!_userConnections.ContainsKey(userId))
            {
                _userConnections[userId] = new HashSet<string>();
            }
            _userConnections[userId].Add(connectionId);
        }
    }

    private static void RemoveUserConnection(string userId, string connectionId)
    {
        lock (_connectionLock)
        {
            if (_userConnections.ContainsKey(userId))
            {
                _userConnections[userId].Remove(connectionId);
                if (_userConnections[userId].Count == 0)
                {
                    _userConnections.Remove(userId);
                }
            }
        }
    }
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