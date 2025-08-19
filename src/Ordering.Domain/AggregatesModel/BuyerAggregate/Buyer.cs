using System.ComponentModel.DataAnnotations;
using eShop.Ordering.Domain.Exceptions;

namespace eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;

public class Buyer
    : Entity, IAggregateRoot
{
    [Required]
    public string IdentityGuid { get; private set; }

    public string Name { get; private set; }

    private List<PaymentMethod> _paymentMethods;
    private List<CryptoWalletAddress> _cryptoWallets;

    public IEnumerable<PaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();
    public IEnumerable<CryptoWalletAddress> CryptoWallets => _cryptoWallets.AsReadOnly();

    protected Buyer()
    {
        _paymentMethods = new List<PaymentMethod>();
        _cryptoWallets = new List<CryptoWalletAddress>();
    }

    public Buyer(string identity, string name) : this()
    {
        IdentityGuid = !string.IsNullOrWhiteSpace(identity) ? identity : throw new ArgumentNullException(nameof(identity));
        Name = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentNullException(nameof(name));
    }

    public PaymentMethod VerifyOrAddPaymentMethod(
        int cardTypeId, string alias, string cardNumber,
        string securityNumber, string cardHolderName, DateTime expiration, int orderId)
    {
        var existingPayment = _paymentMethods
            .SingleOrDefault(p => p.IsEqualTo(cardTypeId, cardNumber, expiration));

        if (existingPayment != null)
        {
            AddDomainEvent(new BuyerAndPaymentMethodVerifiedDomainEvent(this, existingPayment, orderId));

            return existingPayment;
        }

        var payment = new PaymentMethod(cardTypeId, alias, cardNumber, securityNumber, cardHolderName, expiration);

        _paymentMethods.Add(payment);

        AddDomainEvent(new BuyerAndPaymentMethodVerifiedDomainEvent(this, payment, orderId));

        return payment;
    }

    public CryptoWalletAddress AddCryptoWallet(string address, BlockchainNetwork network, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new OrderingDomainException("Crypto wallet address cannot be empty");

        if (network == null)
            throw new OrderingDomainException("Blockchain network cannot be null");

        var walletAddress = new CryptoWalletAddress(address, network, label);

        var existingWallet = _cryptoWallets
            .SingleOrDefault(w => w.IsEqualTo(address, network));

        if (existingWallet != null)
        {
            throw new OrderingDomainException($"Crypto wallet address already exists for {network.Name} network: {address}");
        }

        _cryptoWallets.Add(walletAddress);

        return walletAddress;
    }

    public void RemoveCryptoWallet(string address, BlockchainNetwork network)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new OrderingDomainException("Crypto wallet address cannot be empty");

        if (network == null)
            throw new OrderingDomainException("Blockchain network cannot be null");

        var walletToRemove = _cryptoWallets
            .SingleOrDefault(w => w.IsEqualTo(address, network));

        if (walletToRemove == null)
        {
            throw new OrderingDomainException($"Crypto wallet address not found for {network.Name} network: {address}");
        }

        _cryptoWallets.Remove(walletToRemove);
    }

    public CryptoWalletAddress? GetCryptoWallet(string address, BlockchainNetwork network)
    {
        return _cryptoWallets.SingleOrDefault(w => w.IsEqualTo(address, network));
    }

    public IEnumerable<CryptoWalletAddress> GetCryptoWalletsByNetwork(BlockchainNetwork network)
    {
        return _cryptoWallets.Where(w => w.Network.Equals(network));
    }
}