namespace eShop.CryptoPayment.API.Models;

public class PaymentAddress
{
    public int Id { get; set; }
    
    [Required]
    public string Address { get; set; } = default!;
    
    public int CryptoCurrencyId { get; set; }
    
    public string? PrivateKey { get; set; } // Encrypted storage recommended
    
    public bool IsUsed { get; set; } = false;
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UsedAt { get; set; }
    
    // Navigation properties
    public virtual CryptoCurrency CryptoCurrency { get; set; } = default!;
    public virtual ICollection<CryptoPayment> Payments { get; set; } = new List<CryptoPayment>();
}