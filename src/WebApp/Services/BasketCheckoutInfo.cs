using System.ComponentModel.DataAnnotations;

namespace eShop.WebApp.Services;

public class BasketCheckoutInfo
{
    [Required]
    public string? Street { get; set; }

    [Required]
    public string? City { get; set; }

    [Required]
    public string? State { get; set; }

    [Required]
    public string? Country { get; set; }

    [Required]
    public string? ZipCode { get; set; }

    public string? CardNumber { get; set; }

    public string? CardHolderName { get; set; }

    public string? CardSecurityNumber { get; set; }

    public DateTime? CardExpiration { get; set; }

    public int CardTypeId { get; set; }

    public string? Buyer { get; set; }
    public Guid RequestId { get; set; }

    // Cryptocurrency payment fields
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CreditCard;
    public CryptoCurrency? SelectedCryptoCurrency { get; set; }
    public string? CryptoPaymentAddress { get; set; }
    public decimal? CryptoAmount { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string? TransactionId { get; set; }
    public CryptoTransactionStatus? TransactionStatus { get; set; }
}

public enum PaymentMethod
{
    CreditCard = 0,
    Cryptocurrency = 1
}

public enum CryptoCurrency
{
    Bitcoin = 1,
    Ethereum = 2,
    USDT = 3,
    USDC = 4
}

public enum CryptoTransactionStatus
{
    Pending = 0,
    Confirmed = 1,
    Failed = 2,
    Expired = 3
}
