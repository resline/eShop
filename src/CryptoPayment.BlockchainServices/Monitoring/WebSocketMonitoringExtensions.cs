using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Nethereum.JsonRpc.WebSocketClient;
using Microsoft.Extensions.Logging;

namespace CryptoPayment.BlockchainServices.Monitoring;

public static class WebSocketMonitoringExtensions
{
    public static async Task<bool> TryConnectEthereumWebSocketAsync(this TransactionMonitorService service,
        string rpcEndpoint, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var wsUrl = ConvertToWebSocketUrl(rpcEndpoint);
            using var webSocketClient = new WebSocketClient(wsUrl);
            await webSocketClient.StartAsync();
            await webSocketClient.StopAsync();
            
            logger.LogInformation("Ethereum WebSocket connection test successful: {Url}", wsUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect Ethereum WebSocket to {Endpoint}", rpcEndpoint);
            return false;
        }
    }

    public static async Task<bool> TryConnectCustomWebSocketAsync(this TransactionMonitorService service,
        string rpcEndpoint, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var wsUrl = ConvertToWebSocketUrl(rpcEndpoint);
            using var customWebSocket = new ClientWebSocket();
            await customWebSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
            await customWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", cancellationToken);
            
            logger.LogInformation("Custom WebSocket connection test successful: {Url}", wsUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect custom WebSocket to {Endpoint}", rpcEndpoint);
            return false;
        }
    }

    private static string ConvertToWebSocketUrl(string rpcEndpoint)
    {
        var wsUrl = rpcEndpoint.Replace("http://", "ws://").Replace("https://", "wss://");
        if (!wsUrl.Contains("ws://") && !wsUrl.Contains("wss://"))
        {
            wsUrl = "wss://" + wsUrl;
        }
        return wsUrl;
    }
}