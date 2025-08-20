using CryptoPayment.BlockchainServices.Models;

namespace CryptoPayment.BlockchainServices.Abstractions;

public interface IBlockchainService
{
    string SupportedCurrency { get; }
    bool SupportsTokens { get; }
    
    Task<PaymentAddress> GenerateAddressAsync(string? label = null, CancellationToken cancellationToken = default);
    Task<AddressBalance> GetBalanceAsync(string address, string? tokenContract = null, CancellationToken cancellationToken = default);
    Task<Transaction?> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetTransactionsAsync(string address, int limit = 50, CancellationToken cancellationToken = default);
    Task<TransactionResult> SendTransactionAsync(TransactionRequest request, CancellationToken cancellationToken = default);
    Task<bool> ValidateAddressAsync(string address, CancellationToken cancellationToken = default);
    Task<BlockchainInfo> GetNetworkInfoAsync(CancellationToken cancellationToken = default);
    Task<GasEstimate> EstimateGasAsync(TransactionRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsTransactionConfirmedAsync(string transactionHash, int requiredConfirmations, CancellationToken cancellationToken = default);
}

public interface IBitcoinService : IBlockchainService
{
    Task<string> GetNewAddressAsync(string? label = null, CancellationToken cancellationToken = default);
    Task<decimal> EstimateFeeAsync(int targetBlocks = 6, CancellationToken cancellationToken = default);
    Task<string> SendToAddressAsync(string address, decimal amount, string? privateKey = null, CancellationToken cancellationToken = default);
    Task<int> GetConfirmationsAsync(string transactionHash, CancellationToken cancellationToken = default);
}

public interface IEthereumService : IBlockchainService
{
    Task<string> GetLatestBlockAsync(CancellationToken cancellationToken = default);
    Task<GasEstimate> EstimateGasForTransferAsync(string from, string to, BigInteger amount, string? tokenContract = null, CancellationToken cancellationToken = default);
    Task<string> SendEthAsync(string from, string to, decimal amount, string privateKey, GasEstimate gasEstimate, CancellationToken cancellationToken = default);
    Task<string> SendTokenAsync(string from, string to, decimal amount, string tokenContract, string privateKey, GasEstimate gasEstimate, CancellationToken cancellationToken = default);
    Task<decimal> GetTokenBalanceAsync(string address, string tokenContract, CancellationToken cancellationToken = default);
    Task<bool> IsValidTokenContractAsync(string contractAddress, CancellationToken cancellationToken = default);
}

public interface IBlockchainServiceFactory
{
    IBlockchainService GetService(string currency);
    IBlockchainService GetService(CryptoCurrency currency);
    IBitcoinService GetBitcoinService();
    IEthereumService GetEthereumService();
    IEnumerable<IBlockchainService> GetAllServices();
}

public interface IKeyManager
{
    Task<string> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default);
    Task<string> GetPublicKeyAsync(string privateKey, CancellationToken cancellationToken = default);
    Task<string> SignTransactionAsync(string transactionData, string privateKey, CancellationToken cancellationToken = default);
    Task<bool> ValidatePrivateKeyAsync(string privateKey, string currency, CancellationToken cancellationToken = default);
    Task<string> EncryptPrivateKeyAsync(string privateKey, CancellationToken cancellationToken = default);
    Task<string> DecryptPrivateKeyAsync(string encryptedPrivateKey, CancellationToken cancellationToken = default);
}

public interface IAddressValidator
{
    bool IsValid(string address, string currency);
    bool IsValidBitcoinAddress(string address, NetworkType network = NetworkType.Testnet);
    bool IsValidEthereumAddress(string address);
    bool IsValidTokenContract(string contractAddress);
}