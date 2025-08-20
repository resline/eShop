using System.ComponentModel.DataAnnotations;
using eShop.Ordering.Domain.Exceptions;

namespace eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

public class CryptoPaymentMethod : PaymentMethod
{
    [Required]
    public CryptoWalletAddress WalletAddress { get; private set; }
    
    [Required]
    public CryptoCurrency Currency { get; private set; }
    
    public decimal? MinimumAmount { get; private set; }
    public decimal? MaximumAmount { get; private set; }
    
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    protected CryptoPaymentMethod() : base() { }

    public CryptoPaymentMethod(
        string alias, 
        CryptoWalletAddress walletAddress, 
        CryptoCurrency currency,
        decimal? minimumAmount = null,
        decimal? maximumAmount = null) 
        : base(0, alias, "N/A", "N/A", "Crypto Wallet", DateTime.MaxValue) // Base class requirements, not used for crypto
    {
        WalletAddress = walletAddress ?? throw new OrderingDomainException(nameof(walletAddress));
        Currency = currency ?? throw new OrderingDomainException(nameof(currency));
        
        if (minimumAmount.HasValue && minimumAmount.Value <= 0)
            throw new OrderingDomainException("Minimum amount must be greater than zero");
            
        if (maximumAmount.HasValue && maximumAmount.Value <= 0)
            throw new OrderingDomainException("Maximum amount must be greater than zero");
            
        if (minimumAmount.HasValue && maximumAmount.HasValue && minimumAmount.Value > maximumAmount.Value)
            throw new OrderingDomainException("Minimum amount cannot be greater than maximum amount");

        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetAmountLimits(decimal? minimumAmount, decimal? maximumAmount)
    {
        if (minimumAmount.HasValue && minimumAmount.Value <= 0)
            throw new OrderingDomainException("Minimum amount must be greater than zero");
            
        if (maximumAmount.HasValue && maximumAmount.Value <= 0)
            throw new OrderingDomainException("Maximum amount must be greater than zero");
            
        if (minimumAmount.HasValue && maximumAmount.HasValue && minimumAmount.Value > maximumAmount.Value)
            throw new OrderingDomainException("Minimum amount cannot be greater than maximum amount");

        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public bool CanProcessAmount(decimal amount)
    {
        if (!IsActive) return false;
        
        if (MinimumAmount.HasValue && amount < MinimumAmount.Value) return false;
        if (MaximumAmount.HasValue && amount > MaximumAmount.Value) return false;
        
        return true;
    }

    public bool IsEqualTo(CryptoWalletAddress walletAddress, CryptoCurrency currency)
    {
        return WalletAddress.Equals(walletAddress) && Currency.Equals(currency);
    }

    public override string ToString()
    {
        return $"{Currency.Symbol} - {WalletAddress}";
    }
}