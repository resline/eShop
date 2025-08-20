namespace CryptoPayment.API.Exceptions;

/// <summary>
/// Base class for exceptions that contain user-friendly messages
/// </summary>
public abstract class UserFriendlyException : Exception
{
    public string UserMessage { get; }
    public string? HelpLink { get; }
    public Dictionary<string, object> Context { get; }

    protected UserFriendlyException(
        string userMessage, 
        string? technicalMessage = null, 
        string? helpLink = null,
        Exception? innerException = null) 
        : base(technicalMessage ?? userMessage, innerException)
    {
        UserMessage = userMessage;
        HelpLink = helpLink;
        Context = new Dictionary<string, object>();
    }

    public void AddContext(string key, object value)
    {
        Context[key] = value;
    }
}

/// <summary>
/// Exception for business rule violations in crypto payment operations
/// </summary>
public class CryptoPaymentBusinessException : UserFriendlyException
{
    public CryptoPaymentBusinessException(
        string userMessage, 
        string? technicalMessage = null, 
        Exception? innerException = null)
        : base(userMessage, technicalMessage, "https://docs.cryptopayment.api/business-rules", innerException)
    {
    }
}

/// <summary>
/// Exception for insufficient funds scenarios
/// </summary>
public class InsufficientFundsException : CryptoPaymentBusinessException
{
    public decimal RequiredAmount { get; }
    public decimal AvailableAmount { get; }
    public string Currency { get; }

    public InsufficientFundsException(decimal requiredAmount, decimal availableAmount, string currency)
        : base($"Insufficient funds. Required: {requiredAmount} {currency}, Available: {availableAmount} {currency}",
               $"Payment validation failed: required {requiredAmount} {currency}, but only {availableAmount} {currency} available")
    {
        RequiredAmount = requiredAmount;
        AvailableAmount = availableAmount;
        Currency = currency;
        
        AddContext("RequiredAmount", requiredAmount);
        AddContext("AvailableAmount", availableAmount);
        AddContext("Currency", currency);
    }
}

/// <summary>
/// Exception for invalid cryptocurrency addresses
/// </summary>
public class InvalidAddressException : UserFriendlyException
{
    public string Address { get; }
    public string Currency { get; }

    public InvalidAddressException(string address, string currency, string? reason = null)
        : base($"The {currency} address is invalid. Please check the address and try again.",
               $"Invalid {currency} address: {address}. Reason: {reason ?? "Unknown"}")
    {
        Address = address;
        Currency = currency;
        
        AddContext("Address", address);
        AddContext("Currency", currency);
        if (!string.IsNullOrEmpty(reason))
        {
            AddContext("ValidationFailureReason", reason);
        }
    }
}

/// <summary>
/// Exception for transaction timeout scenarios
/// </summary>
public class TransactionTimeoutException : UserFriendlyException
{
    public string TransactionId { get; }
    public TimeSpan Timeout { get; }

    public TransactionTimeoutException(string transactionId, TimeSpan timeout)
        : base("The transaction is taking longer than expected. Please check back later or contact support.",
               $"Transaction {transactionId} timed out after {timeout}")
    {
        TransactionId = transactionId;
        Timeout = timeout;
        
        AddContext("TransactionId", transactionId);
        AddContext("TimeoutDuration", timeout);
    }
}

/// <summary>
/// Exception for payment processing errors
/// </summary>
public class PaymentProcessingException : CryptoPaymentBusinessException
{
    public string PaymentId { get; }
    public string Stage { get; }

    public PaymentProcessingException(string paymentId, string stage, string userMessage, Exception? innerException = null)
        : base(userMessage, $"Payment processing failed at stage '{stage}' for payment {paymentId}", innerException)
    {
        PaymentId = paymentId;
        Stage = stage;
        
        AddContext("PaymentId", paymentId);
        AddContext("ProcessingStage", stage);
    }
}

/// <summary>
/// Exception for rate limiting scenarios
/// </summary>
public class RateLimitExceededException : UserFriendlyException
{
    public TimeSpan RetryAfter { get; }
    public string LimitType { get; }

    public RateLimitExceededException(TimeSpan retryAfter, string limitType = "requests")
        : base($"You're making too many {limitType}. Please wait {retryAfter.TotalMinutes:F1} minutes before trying again.",
               $"Rate limit exceeded for {limitType}. Retry after: {retryAfter}")
    {
        RetryAfter = retryAfter;
        LimitType = limitType;
        
        AddContext("RetryAfter", retryAfter);
        AddContext("LimitType", limitType);
    }
}

/// <summary>
/// Exception for external service failures
/// </summary>
public class ExternalServiceException : UserFriendlyException
{
    public string ServiceName { get; }
    public string Operation { get; }

    public ExternalServiceException(string serviceName, string operation, Exception? innerException = null)
        : base($"The {serviceName} service is temporarily unavailable. Please try again in a few minutes.",
               $"External service '{serviceName}' failed during operation '{operation}'", 
               "https://docs.cryptopayment.api/service-status",
               innerException)
    {
        ServiceName = serviceName;
        Operation = operation;
        
        AddContext("ServiceName", serviceName);
        AddContext("Operation", operation);
    }
}

/// <summary>
/// Exception for configuration-related errors
/// </summary>
public class ConfigurationException : UserFriendlyException
{
    public string ConfigurationKey { get; }

    public ConfigurationException(string configurationKey, string userMessage)
        : base(userMessage, $"Configuration error for key: {configurationKey}")
    {
        ConfigurationKey = configurationKey;
        AddContext("ConfigurationKey", configurationKey);
    }
}

/// <summary>
/// Exception for blockchain-related errors
/// </summary>
public class BlockchainException : ExternalServiceException
{
    public string? TransactionHash { get; }
    public string? BlockNumber { get; }

    public BlockchainException(string operation, string? transactionHash = null, Exception? innerException = null)
        : base("Blockchain Network", operation, innerException)
    {
        TransactionHash = transactionHash;
        
        if (!string.IsNullOrEmpty(transactionHash))
        {
            AddContext("TransactionHash", transactionHash);
        }
    }
}

/// <summary>
/// Exception for validation errors with detailed field information
/// </summary>
public class ValidationException : UserFriendlyException
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException(Dictionary<string, string[]> errors)
        : base("Please correct the following errors and try again:",
               $"Validation failed for {errors.Count} field(s)")
    {
        Errors = errors;
        AddContext("ValidationErrors", errors);
    }

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = new[] { error } })
    {
    }
}