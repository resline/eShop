namespace eShop.CryptoPayment.API.Models;

public class CreatePaymentRequest
{
    [Required]
    public string PaymentId { get; set; } = default!;
    
    [Required]
    public CryptoCurrencyType CryptoCurrency { get; set; }
    
    [Required]
    [Range(0.00000001, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
    
    public string? BuyerId { get; set; }
    
    public int? ExpirationMinutes { get; set; } = 30; // Default 30 minutes
    
    public Dictionary<string, object>? Metadata { get; set; }
}

public class PaymentResponse
{
    public int Id { get; set; }
    public string PaymentId { get; set; } = default!;
    public string CryptoCurrency { get; set; } = default!;
    public string PaymentAddress { get; set; } = default!;
    public decimal RequestedAmount { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? TransactionHash { get; set; }
    public int? Confirmations { get; set; }
    public int RequiredConfirmations { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class WebhookPayload
{
    public string EventType { get; set; } = default!;
    public PaymentResponse Payment { get; set; } = default!;
    public DateTime Timestamp { get; set; }
    public string Signature { get; set; } = default!;
}