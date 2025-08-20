namespace CryptoPayment.BlockchainServices.Exceptions;

public class BlockchainException : Exception
{
    public string Currency { get; }
    public string? TransactionHash { get; }
    public string? Address { get; }

    public BlockchainException(string currency, string message) : base(message)
    {
        Currency = currency;
    }

    public BlockchainException(string currency, string message, Exception innerException) : base(message, innerException)
    {
        Currency = currency;
    }

    public BlockchainException(string currency, string message, string? transactionHash = null, string? address = null) 
        : base(message)
    {
        Currency = currency;
        TransactionHash = transactionHash;
        Address = address;
    }

    public BlockchainException(string currency, string message, Exception innerException, string? transactionHash = null, string? address = null) 
        : base(message, innerException)
    {
        Currency = currency;
        TransactionHash = transactionHash;
        Address = address;
    }
}

public class InvalidAddressException : BlockchainException
{
    public InvalidAddressException(string currency, string address) 
        : base(currency, $"Invalid {currency} address: {address}", address: address)
    {
    }

    public InvalidAddressException(string currency, string address, Exception innerException) 
        : base(currency, $"Invalid {currency} address: {address}", innerException, address: address)
    {
    }
}

public class InsufficientFundsException : BlockchainException
{
    public decimal RequiredAmount { get; }
    public decimal AvailableAmount { get; }

    public InsufficientFundsException(string currency, decimal requiredAmount, decimal availableAmount, string? address = null) 
        : base(currency, $"Insufficient {currency} funds: required {requiredAmount}, available {availableAmount}", address: address)
    {
        RequiredAmount = requiredAmount;
        AvailableAmount = availableAmount;
    }
}

public class TransactionNotFoundException : BlockchainException
{
    public TransactionNotFoundException(string currency, string transactionHash) 
        : base(currency, $"Transaction not found: {transactionHash}", transactionHash: transactionHash)
    {
    }

    public TransactionNotFoundException(string currency, string transactionHash, Exception innerException) 
        : base(currency, $"Transaction not found: {transactionHash}", innerException, transactionHash: transactionHash)
    {
    }
}

public class TransactionFailedException : BlockchainException
{
    public string Reason { get; }

    public TransactionFailedException(string currency, string transactionHash, string reason) 
        : base(currency, $"Transaction failed: {reason}", transactionHash: transactionHash)
    {
        Reason = reason;
    }

    public TransactionFailedException(string currency, string transactionHash, string reason, Exception innerException) 
        : base(currency, $"Transaction failed: {reason}", innerException, transactionHash: transactionHash)
    {
        Reason = reason;
    }
}

public class NetworkConnectionException : BlockchainException
{
    public string Endpoint { get; }

    public NetworkConnectionException(string currency, string endpoint, string message) 
        : base(currency, $"Network connection failed for {currency} at {endpoint}: {message}")
    {
        Endpoint = endpoint;
    }

    public NetworkConnectionException(string currency, string endpoint, string message, Exception innerException) 
        : base(currency, $"Network connection failed for {currency} at {endpoint}: {message}", innerException)
    {
        Endpoint = endpoint;
    }
}

public class GasEstimationException : BlockchainException
{
    public GasEstimationException(string currency, string message) 
        : base(currency, $"Gas estimation failed for {currency}: {message}")
    {
    }

    public GasEstimationException(string currency, string message, Exception innerException) 
        : base(currency, $"Gas estimation failed for {currency}: {message}", innerException)
    {
    }
}

public class KeyManagementException : Exception
{
    public string Operation { get; }

    public KeyManagementException(string operation, string message) : base($"Key management operation '{operation}' failed: {message}")
    {
        Operation = operation;
    }

    public KeyManagementException(string operation, string message, Exception innerException) 
        : base($"Key management operation '{operation}' failed: {message}", innerException)
    {
        Operation = operation;
    }
}

public class ProviderException : Exception
{
    public string Provider { get; }
    public string? RequestId { get; }

    public ProviderException(string provider, string message) : base($"Provider '{provider}' error: {message}")
    {
        Provider = provider;
    }

    public ProviderException(string provider, string message, Exception innerException) 
        : base($"Provider '{provider}' error: {message}", innerException)
    {
        Provider = provider;
    }

    public ProviderException(string provider, string message, string? requestId = null) 
        : base($"Provider '{provider}' error: {message}")
    {
        Provider = provider;
        RequestId = requestId;
    }
}

public class ConfigurationException : Exception
{
    public string ConfigurationKey { get; }

    public ConfigurationException(string configurationKey, string message) 
        : base($"Configuration error for '{configurationKey}': {message}")
    {
        ConfigurationKey = configurationKey;
    }

    public ConfigurationException(string configurationKey, string message, Exception innerException) 
        : base($"Configuration error for '{configurationKey}': {message}", innerException)
    {
        ConfigurationKey = configurationKey;
    }
}