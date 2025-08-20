using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CryptoPayment.BlockchainServices.Monitoring;

public static class TransactionMonitorServiceExtensions
{
    public static async Task<bool> ConnectEthereumWebSocketAsync(this TransactionMonitorService service, 
        string rpcEndpoint, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var wsUrl = rpcEndpoint.Replace("http://", "ws://").Replace("https://", "wss://");
            if (!wsUrl.Contains("ws://") && !wsUrl.Contains("wss://"))
            {
                wsUrl = "wss://" + wsUrl;
            }

            logger.LogInformation("Attempting to connect Ethereum WebSocket to {Url}", wsUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect Ethereum WebSocket");
            return false;
        }
    }

    public static async Task<bool> ConnectCustomWebSocketAsync(this TransactionMonitorService service,
        string rpcEndpoint, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var wsUrl = rpcEndpoint.Replace("http://", "ws://").Replace("https://", "wss://");
            if (!wsUrl.Contains("ws://") && !wsUrl.Contains("wss://"))
            {
                wsUrl = "wss://" + wsUrl;
            }

            using var webSocket = new ClientWebSocket();
            await webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
            
            logger.LogInformation("Custom WebSocket connected to {Url}", wsUrl);
            
            // Close the test connection
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect custom WebSocket");
            return false;
        }
    }
}