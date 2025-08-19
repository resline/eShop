namespace eShop.CryptoPayment.API.Models;

public class CryptoCurrency
{
    public int Id { get; set; }
    
    [Required]
    public string Symbol { get; set; } = default!;
    
    [Required]
    public string Name { get; set; } = default!;
    
    public int Decimals { get; set; }
    
    public string? NetworkName { get; set; }
    
    public string? ContractAddress { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<CryptoPayment> Payments { get; set; } = new List<CryptoPayment>();
    public virtual ICollection<PaymentAddress> PaymentAddresses { get; set; } = new List<PaymentAddress>();
    
    // Predefined cryptocurrencies
    public static readonly CryptoCurrency Bitcoin = new()
    {
        Id = 1,
        Symbol = "BTC",
        Name = "Bitcoin",
        Decimals = 8,
        NetworkName = "Bitcoin",
        IsActive = true
    };
    
    public static readonly CryptoCurrency Ethereum = new()
    {
        Id = 2,
        Symbol = "ETH",
        Name = "Ethereum",
        Decimals = 18,
        NetworkName = "Ethereum",
        IsActive = true
    };
}

public enum CryptoCurrencyType
{
    Bitcoin = 1,
    Ethereum = 2
}