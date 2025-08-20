using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using eShop.CryptoPayment.API.Hubs;

namespace eShop.CryptoPayment.API.Services;

// Connection tracking interfaces and implementation
public interface IConnectionTracker
{
    Task AddConnectionAsync(string connectionId, string userId);
    Task RemoveConnectionAsync(string connectionId);
    Task DisconnectConnectionAsync(string connectionId, string reason);
    IReadOnlyList<UserConnection> GetConnectionsForUser(string userId);
    int GetTotalConnections();
    IReadOnlyDictionary<string, int> GetConnectionStatistics();
    Task CleanupStaleConnectionsAsync();
}

public class ConnectionTracker : IConnectionTracker
{
    private readonly ConcurrentDictionary<string, UserConnection> _connections = new();
    private readonly ConcurrentDictionary<string, List<string>> _userConnections = new();
    private readonly IHubContext<PaymentStatusHub> _hubContext;
    private readonly ILogger<ConnectionTracker> _logger;
    private readonly object _lock = new();
    private readonly Timer _cleanupTimer;

    public ConnectionTracker(
        IHubContext<PaymentStatusHub> hubContext,
        ILogger<ConnectionTracker> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        
        // Run cleanup every 5 minutes
        _cleanupTimer = new Timer(async _ => await CleanupStaleConnectionsAsync(), 
            null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task AddConnectionAsync(string connectionId, string userId)
    {
        var connection = new UserConnection(connectionId, userId, DateTime.UtcNow);
        
        lock (_lock)
        {
            _connections[connectionId] = connection;
            
            if (!_userConnections.TryGetValue(userId, out var userConnectionList))
            {
                userConnectionList = new List<string>();
                _userConnections[userId] = userConnectionList;
            }
            
            userConnectionList.Add(connectionId);
        }
        
        _logger.LogDebug("Added connection {ConnectionId} for user {UserId}", connectionId, userId);
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(string connectionId)
    {
        lock (_lock)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                if (_userConnections.TryGetValue(connection.UserId, out var userConnectionList))
                {
                    userConnectionList.Remove(connectionId);
                    
                    if (userConnectionList.Count == 0)
                    {
                        _userConnections.TryRemove(connection.UserId, out _);
                    }
                }
                
                _logger.LogDebug("Removed connection {ConnectionId} for user {UserId}", connectionId, connection.UserId);
            }
        }
        
        return Task.CompletedTask;
    }

    public async Task DisconnectConnectionAsync(string connectionId, string reason)
    {
        try
        {
            await _hubContext.Clients.Client(connectionId).SendAsync("ConnectionLimitExceeded", reason);
            await Task.Delay(100); // Give time for message to be sent
            
            // Note: In SignalR, we can't directly disconnect a client from the server side
            // The client should handle the "ConnectionLimitExceeded" message and disconnect itself
            _logger.LogInformation("Sent disconnection message to {ConnectionId}: {Reason}", connectionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send disconnection message to {ConnectionId}", connectionId);
        }
    }

    public IReadOnlyList<UserConnection> GetConnectionsForUser(string userId)
    {
        lock (_lock)
        {
            if (!_userConnections.TryGetValue(userId, out var connectionIds))
            {
                return Array.Empty<UserConnection>();
            }
            
            return connectionIds
                .Where(id => _connections.ContainsKey(id))
                .Select(id => _connections[id])
                .ToList();
        }
    }

    public int GetTotalConnections()
    {
        return _connections.Count;
    }

    public IReadOnlyDictionary<string, int> GetConnectionStatistics()
    {
        lock (_lock)
        {
            return _userConnections.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Count
            );
        }
    }

    public Task CleanupStaleConnectionsAsync()
    {
        var cutoff = DateTime.UtcNow.AddHours(-2); // Remove connections older than 2 hours
        var staleConnections = new List<string>();
        
        lock (_lock)
        {
            foreach (var connection in _connections.Values)
            {
                if (connection.ConnectedAt < cutoff)
                {
                    staleConnections.Add(connection.ConnectionId);
                }
            }
        }
        
        if (staleConnections.Count > 0)
        {
            _logger.LogInformation("Cleaning up {Count} stale connections", staleConnections.Count);
            
            foreach (var connectionId in staleConnections)
            {
                _ = RemoveConnectionAsync(connectionId);
            }
        }
        
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

public record UserConnection(
    string ConnectionId,
    string UserId,
    DateTime ConnectedAt
);

// Connection statistics for monitoring
public class ConnectionStatistics
{
    public int TotalConnections { get; set; }
    public Dictionary<string, int> ConnectionsPerUser { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public int MaxConnectionsPerUser { get; set; } = 5;
    public List<string> UsersAtLimit { get; set; } = new();
    
    public double AverageConnectionsPerUser => 
        ConnectionsPerUser.Count > 0 ? (double)TotalConnections / ConnectionsPerUser.Count : 0;
        
    public int UsersExceedingThreshold(int threshold) => 
        ConnectionsPerUser.Count(kvp => kvp.Value >= threshold);
}