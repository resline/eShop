using System.Security.Cryptography;
using System.Text;
using RestSharp;
using CryptoPayment.BlockchainServices.Configuration;
using CryptoPayment.BlockchainServices.Models;

namespace CryptoPayment.BlockchainServices.Providers;

public class CoinbaseCommerceClient : ICoinbaseCommerceClient
{
    private readonly ILogger<CoinbaseCommerceClient> _logger;
    private readonly CoinbaseCommerceOptions _options;
    private readonly RestClient _restClient;

    public CoinbaseCommerceClient(ILogger<CoinbaseCommerceClient> logger, IOptions<BlockchainOptions> options)
    {
        _logger = logger;
        _options = options.Value.Providers.CoinbaseCommerce;
        
        _restClient = new RestClient(_options.BaseUrl);
        _restClient.AddDefaultHeader("X-CC-Api-Key", _options.ApiKey);
        _restClient.AddDefaultHeader("X-CC-Version", "2018-03-22");
    }

    public async Task<CoinbaseChargeResponse> CreateChargeAsync(CoinbaseChargeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var restRequest = new RestRequest("charges", Method.Post);
            restRequest.AddJsonBody(request);

            var response = await _restClient.ExecuteAsync<CoinbaseChargeResponse>(restRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create Coinbase charge: {Error}", response.ErrorMessage);
                throw new InvalidOperationException($"Failed to create Coinbase charge: {response.ErrorMessage}");
            }

            _logger.LogInformation("Created Coinbase charge {ChargeId} for amount {Amount}", response.Data?.Data?.Id, request.LocalPrice?.Amount);
            return response.Data!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Coinbase charge");
            throw new InvalidOperationException("Error creating Coinbase charge", ex);
        }
    }

    public async Task<CoinbaseChargeResponse> GetChargeAsync(string chargeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var restRequest = new RestRequest($"charges/{chargeId}", Method.Get);
            var response = await _restClient.ExecuteAsync<CoinbaseChargeResponse>(restRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get Coinbase charge {ChargeId}: {Error}", chargeId, response.ErrorMessage);
                throw new InvalidOperationException($"Failed to get Coinbase charge: {response.ErrorMessage}");
            }

            return response.Data!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Coinbase charge {ChargeId}", chargeId);
            throw new InvalidOperationException($"Error getting Coinbase charge {chargeId}", ex);
        }
    }

    public async Task<IEnumerable<CoinbaseChargeResponse>> GetChargesAsync(int limit = 25, CancellationToken cancellationToken = default)
    {
        try
        {
            var restRequest = new RestRequest("charges", Method.Get);
            restRequest.AddParameter("limit", limit);

            var response = await _restClient.ExecuteAsync<CoinbaseChargeListResponse>(restRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get Coinbase charges: {Error}", response.ErrorMessage);
                throw new InvalidOperationException($"Failed to get Coinbase charges: {response.ErrorMessage}");
            }

            return response.Data?.Data ?? Array.Empty<CoinbaseChargeResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Coinbase charges");
            throw new InvalidOperationException("Error getting Coinbase charges", ex);
        }
    }

    public bool ValidateWebhook(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogWarning("Webhook secret not configured");
            return false;
        }

        try
        {
            var expectedSignature = ComputeSignature(payload, _options.WebhookSecret);
            return signature.Equals(expectedSignature, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature");
            return false;
        }
    }

    private static string ComputeSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

public interface ICoinbaseCommerceClient
{
    Task<CoinbaseChargeResponse> CreateChargeAsync(CoinbaseChargeRequest request, CancellationToken cancellationToken = default);
    Task<CoinbaseChargeResponse> GetChargeAsync(string chargeId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CoinbaseChargeResponse>> GetChargesAsync(int limit = 25, CancellationToken cancellationToken = default);
    bool ValidateWebhook(string payload, string signature);
}

public record CoinbaseChargeRequest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public CoinbaseMoney LocalPrice { get; init; } = new();
    public string? PricingType { get; init; } = "fixed_price";
    public Dictionary<string, object> Metadata { get; init; } = new();
    public string? RedirectUrl { get; init; }
    public string? CancelUrl { get; init; }
}

public record CoinbaseMoney
{
    public string Amount { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
}

public record CoinbaseChargeResponse
{
    public CoinbaseCharge? Data { get; init; }
}

public record CoinbaseChargeListResponse
{
    public IEnumerable<CoinbaseCharge>? Data { get; init; }
}

public record CoinbaseCharge
{
    public string Id { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public CoinbaseMoney LocalPrice { get; init; } = new();
    public Dictionary<string, CoinbasePaymentAmount> PricingCurrency { get; init; } = new();
    public Dictionary<string, object> Addresses { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string HostedUrl { get; init; } = string.Empty;
    public Dictionary<string, object> Metadata { get; init; } = new();
    public List<CoinbasePayment> Payments { get; init; } = new();
    public Dictionary<string, object> Timeline { get; init; } = new();
}

public record CoinbasePaymentAmount
{
    public string Amount { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
}

public record CoinbasePayment
{
    public string Network { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public CoinbasePaymentAmount Value { get; init; } = new();
}