using System.Security.Cryptography;
using System.Text;
using RestSharp;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.BlockchainServices.Providers;

public class BitPayClient : IBitPayClient
{
    private readonly ILogger<BitPayClient> _logger;
    private readonly BitPayOptions _options;
    private readonly RestClient _restClient;

    public BitPayClient(ILogger<BitPayClient> logger, IOptions<BlockchainOptions> options)
    {
        _logger = logger;
        _options = options.Value.Providers.BitPay;
        
        _restClient = new RestClient(_options.BaseUrl);
        _restClient.AddDefaultHeader("X-Accept-Version", "2.0.0");
        _restClient.AddDefaultHeader("Content-Type", "application/json");
    }

    public async Task<BitPayInvoiceResponse> CreateInvoiceAsync(BitPayInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var restRequest = new RestRequest("invoices", Method.Post);
            restRequest.AddJsonBody(request);
            
            // Add authentication header
            var requestBody = JsonSerializer.Serialize(request);
            var signature = SignRequest("POST", "/invoices", requestBody);
            restRequest.AddHeader("X-Signature", signature);
            restRequest.AddHeader("X-Identity", GetPublicKeyFromPrivateKey(_options.PrivateKey!));

            var response = await _restClient.ExecuteAsync<BitPayInvoiceResponse>(restRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create BitPay invoice: {Error}", response.ErrorMessage);
                throw new InvalidOperationException($"Failed to create BitPay invoice: {response.ErrorMessage}");
            }

            _logger.LogInformation("Created BitPay invoice {InvoiceId} for amount {Amount}", response.Data?.Data?.Id, request.Price);
            return response.Data!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating BitPay invoice");
            throw new InvalidOperationException("Error creating BitPay invoice", ex);
        }
    }

    public async Task<BitPayInvoiceResponse> GetInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var restRequest = new RestRequest($"invoices/{invoiceId}", Method.Get);
            
            // Add authentication header
            var signature = SignRequest("GET", $"/invoices/{invoiceId}", string.Empty);
            restRequest.AddHeader("X-Signature", signature);
            restRequest.AddHeader("X-Identity", GetPublicKeyFromPrivateKey(_options.PrivateKey!));

            var response = await _restClient.ExecuteAsync<BitPayInvoiceResponse>(restRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get BitPay invoice {InvoiceId}: {Error}", invoiceId, response.ErrorMessage);
                throw new InvalidOperationException($"Failed to get BitPay invoice: {response.ErrorMessage}");
            }

            return response.Data!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting BitPay invoice {InvoiceId}", invoiceId);
            throw new InvalidOperationException($"Error getting BitPay invoice {invoiceId}", ex);
        }
    }

    public async Task<IEnumerable<BitPayInvoiceResponse>> GetInvoicesAsync(DateTime? dateStart = null, DateTime? dateEnd = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var restRequest = new RestRequest("invoices", Method.Get);
            
            if (dateStart.HasValue)
                restRequest.AddParameter("dateStart", dateStart.Value.ToString("yyyy-MM-dd"));
            
            if (dateEnd.HasValue)
                restRequest.AddParameter("dateEnd", dateEnd.Value.ToString("yyyy-MM-dd"));
            
            // Add authentication header
            var signature = SignRequest("GET", "/invoices", string.Empty);
            restRequest.AddHeader("X-Signature", signature);
            restRequest.AddHeader("X-Identity", GetPublicKeyFromPrivateKey(_options.PrivateKey!));

            var response = await _restClient.ExecuteAsync<BitPayInvoiceListResponse>(restRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get BitPay invoices: {Error}", response.ErrorMessage);
                throw new InvalidOperationException($"Failed to get BitPay invoices: {response.ErrorMessage}");
            }

            return response.Data?.Data ?? Array.Empty<BitPayInvoiceResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting BitPay invoices");
            throw new InvalidOperationException("Error getting BitPay invoices", ex);
        }
    }

    public bool ValidateWebhook(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_options.PrivateKey))
        {
            _logger.LogWarning("Private key not configured for webhook validation");
            return false;
        }

        try
        {
            // BitPay webhook validation logic would go here
            // This is a simplified implementation
            return !string.IsNullOrEmpty(signature);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating BitPay webhook signature");
            return false;
        }
    }

    private string SignRequest(string method, string uri, string body)
    {
        if (string.IsNullOrEmpty(_options.PrivateKey))
        {
            throw new InvalidOperationException("Private key not configured");
        }

        var fullUri = _options.BaseUrl + uri;
        var message = $"{method}{fullUri}{body}";
        
        // This is a simplified signing implementation
        // In production, you'd use proper ECDSA signing with the BitPay private key format
        using var sha256 = SHA256.Create();
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hashBytes = sha256.ComputeHash(messageBytes);
        
        return Convert.ToBase64String(hashBytes);
    }

    private static string GetPublicKeyFromPrivateKey(string privateKey)
    {
        // This is a simplified implementation
        // In production, you'd derive the public key from the private key using proper cryptographic methods
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(privateKey));
    }
}

public interface IBitPayClient
{
    Task<BitPayInvoiceResponse> CreateInvoiceAsync(BitPayInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<BitPayInvoiceResponse> GetInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BitPayInvoiceResponse>> GetInvoicesAsync(DateTime? dateStart = null, DateTime? dateEnd = null, CancellationToken cancellationToken = default);
    bool ValidateWebhook(string payload, string signature);
}

public record BitPayInvoiceRequest
{
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public string OrderId { get; init; } = string.Empty;
    public string ItemDesc { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string NotificationEmail { get; init; } = string.Empty;
    public string NotificationURL { get; init; } = string.Empty;
    public string RedirectURL { get; init; } = string.Empty;
    public Dictionary<string, object> PosData { get; init; } = new();
    public bool TransactionSpeed { get; init; } = true;
    public bool FullNotifications { get; init; } = true;
}

public record BitPayInvoiceResponse
{
    public BitPayInvoice? Data { get; init; }
}

public record BitPayInvoiceListResponse
{
    public IEnumerable<BitPayInvoice>? Data { get; init; }
}

public record BitPayInvoice
{
    public string Id { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public DateTime InvoiceTime { get; init; }
    public DateTime ExpirationTime { get; init; }
    public DateTime CurrentTime { get; init; }
    public string PaymentCurrencies { get; init; } = string.Empty;
    public Dictionary<string, object> ExceptionStatus { get; init; } = new();
    public Dictionary<string, object> PaymentTotals { get; init; } = new();
    public Dictionary<string, object> PaymentDisplayTotals { get; init; } = new();
    public Dictionary<string, object> PaymentSubTotals { get; init; } = new();
    public Dictionary<string, object> PaymentDisplaySubTotals { get; init; } = new();
    public bool AcceptanceWindow { get; init; }
    public string Token { get; init; } = string.Empty;
}