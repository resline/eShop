namespace eShop.CryptoPayment.API.Models;

public class CryptoPayment
{
    public int Id { get; set; }
    
    [Required]
    public string PaymentId { get; set; } = default!; // External reference (e.g., OrderId)
    
    public int CryptoCurrencyId { get; set; }
    
    public int PaymentAddressId { get; set; }
    
    public decimal RequestedAmount { get; set; }
    
    public decimal? ReceivedAmount { get; set; }
    
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    
    public string? TransactionHash { get; set; }
    
    public int? BlockNumber { get; set; }
    
    public int? Confirmations { get; set; }
    
    public int RequiredConfirmations { get; set; } = 6; // Default for Bitcoin
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public DateTime? ExpiresAt { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public string? BuyerId { get; set; }
    
    // Additional metadata as JSON
    public string? Metadata { get; set; }
    
    // Navigation properties
    public virtual CryptoCurrency CryptoCurrency { get; set; } = default!;
    public virtual PaymentAddress PaymentAddress { get; set; } = default!;
}

public enum PaymentStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Confirmed = 4,
    Failed = 5,
    Expired = 6,
    Cancelled = 7
}