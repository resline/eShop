using System.Text.RegularExpressions;

namespace eShop.WebApp.Services;

public interface ICryptoValidationService
{
    ValidationResult ValidatePaymentAmount(decimal amount, CryptoCurrency currency);
    ValidationResult ValidateCryptoAddress(string address, CryptoCurrency currency);
    ValidationResult ValidatePaymentId(string paymentId);
    ValidationResult ValidateExpirationMinutes(int? expirationMinutes);
    Task<ValidationResult> ValidatePaymentRequestAsync(CreateCryptoPaymentRequest request);
}

public class CryptoValidationService : ICryptoValidationService
{
    private readonly ILogger<CryptoValidationService> _logger;

    private static readonly Dictionary<CryptoCurrency, (decimal Min, decimal Max, int Decimals)> CurrencyLimits = new()
    {
        { CryptoCurrency.Bitcoin, (0.00000001m, 10m, 8) },      // 1 satoshi to 10 BTC, 8 decimals
        { CryptoCurrency.Ethereum, (0.000000000000000001m, 100m, 18) }, // 1 wei to 100 ETH, 18 decimals
        { CryptoCurrency.USDT, (0.01m, 100000m, 6) },          // 1 cent to 100k USDT, 6 decimals
        { CryptoCurrency.USDC, (0.01m, 100000m, 6) }           // 1 cent to 100k USDC, 6 decimals
    };

    public CryptoValidationService(ILogger<CryptoValidationService> logger)
    {
        _logger = logger;
    }

    public ValidationResult ValidatePaymentAmount(decimal amount, CryptoCurrency currency)
    {
        var errors = new List<string>();

        // Check if amount is positive
        if (amount <= 0)
        {
            errors.Add("Amount must be greater than 0");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        // Check currency-specific limits
        if (CurrencyLimits.TryGetValue(currency, out var limits))
        {
            if (amount < limits.Min)
            {
                errors.Add($"Amount must be at least {limits.Min:F8} {currency}");
            }

            if (amount > limits.Max)
            {
                errors.Add($"Amount cannot exceed {limits.Max:F2} {currency}");
            }

            // Check decimal precision
            var decimalPlaces = GetDecimalPlaces(amount);
            if (decimalPlaces > limits.Decimals)
            {
                errors.Add($"{currency} supports up to {limits.Decimals} decimal places");
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    public ValidationResult ValidateCryptoAddress(string address, CryptoCurrency currency)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(address))
        {
            errors.Add("Payment address is required");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        var isValid = currency switch
        {
            CryptoCurrency.Bitcoin => ValidateBitcoinAddress(address, errors),
            CryptoCurrency.Ethereum => ValidateEthereumAddress(address, errors),
            CryptoCurrency.USDT => ValidateEthereumAddress(address, errors), // USDT on Ethereum
            CryptoCurrency.USDC => ValidateEthereumAddress(address, errors), // USDC on Ethereum
            _ => false
        };

        if (!isValid && errors.Count == 0)
        {
            errors.Add($"Invalid {currency} address format");
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    public ValidationResult ValidatePaymentId(string paymentId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(paymentId))
        {
            errors.Add("Payment ID is required");
        }
        else
        {
            if (paymentId.Length < 1 || paymentId.Length > 100)
            {
                errors.Add("Payment ID must be between 1 and 100 characters");
            }

            if (!Regex.IsMatch(paymentId, @"^[a-zA-Z0-9\-_]+$"))
            {
                errors.Add("Payment ID can only contain letters, numbers, hyphens, and underscores");
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    public ValidationResult ValidateExpirationMinutes(int? expirationMinutes)
    {
        var errors = new List<string>();

        if (expirationMinutes.HasValue)
        {
            if (expirationMinutes.Value < 5)
            {
                errors.Add("Expiration must be at least 5 minutes");
            }
            else if (expirationMinutes.Value > 1440) // 24 hours
            {
                errors.Add("Expiration cannot exceed 24 hours");
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    public async Task<ValidationResult> ValidatePaymentRequestAsync(CreateCryptoPaymentRequest request)
    {
        var allErrors = new List<string>();

        // Validate payment ID
        var paymentIdResult = ValidatePaymentId(request.PaymentId);
        allErrors.AddRange(paymentIdResult.Errors);

        // Validate amount
        var amountResult = ValidatePaymentAmount(request.Amount, request.Currency);
        allErrors.AddRange(amountResult.Errors);

        // Validate expiration
        var expirationResult = ValidateExpirationMinutes(request.ExpirationMinutes);
        allErrors.AddRange(expirationResult.Errors);

        // Validate buyer ID if provided
        if (!string.IsNullOrEmpty(request.BuyerId))
        {
            if (request.BuyerId.Length > 100)
            {
                allErrors.Add("Buyer ID cannot exceed 100 characters");
            }

            if (!Regex.IsMatch(request.BuyerId, @"^[a-zA-Z0-9\-_@.]+$"))
            {
                allErrors.Add("Buyer ID contains invalid characters");
            }
        }

        // Log validation results
        if (allErrors.Any())
        {
            _logger.LogWarning("Payment request validation failed for PaymentId: {PaymentId}. Errors: {Errors}",
                request.PaymentId, string.Join(", ", allErrors));
        }

        return new ValidationResult { IsValid = allErrors.Count == 0, Errors = allErrors };
    }

    private static bool ValidateBitcoinAddress(string address, List<string> errors)
    {
        // Legacy addresses (starts with 1)
        if (address.StartsWith("1"))
        {
            if (address.Length < 26 || address.Length > 35)
            {
                errors.Add("Bitcoin legacy address must be between 26 and 35 characters");
                return false;
            }
            
            if (!Regex.IsMatch(address, @"^[1][a-km-zA-HJ-NP-Z1-9]{25,34}$"))
            {
                errors.Add("Invalid Bitcoin legacy address format");
                return false;
            }
            
            return true;
        }

        // Script hash addresses (starts with 3)
        if (address.StartsWith("3"))
        {
            if (address.Length < 26 || address.Length > 35)
            {
                errors.Add("Bitcoin script address must be between 26 and 35 characters");
                return false;
            }
            
            if (!Regex.IsMatch(address, @"^[3][a-km-zA-HJ-NP-Z1-9]{25,34}$"))
            {
                errors.Add("Invalid Bitcoin script address format");
                return false;
            }
            
            return true;
        }

        // Bech32 addresses (starts with bc1)
        if (address.StartsWith("bc1"))
        {
            if (address.Length < 42 || address.Length > 62)
            {
                errors.Add("Bitcoin bech32 address must be between 42 and 62 characters");
                return false;
            }
            
            if (!Regex.IsMatch(address, @"^bc1[a-z0-9]{39,59}$"))
            {
                errors.Add("Invalid Bitcoin bech32 address format");
                return false;
            }
            
            return true;
        }

        errors.Add("Bitcoin address must start with 1, 3, or bc1");
        return false;
    }

    private static bool ValidateEthereumAddress(string address, List<string> errors)
    {
        if (!address.StartsWith("0x"))
        {
            errors.Add("Ethereum address must start with 0x");
            return false;
        }

        if (address.Length != 42)
        {
            errors.Add("Ethereum address must be exactly 42 characters long");
            return false;
        }

        if (!Regex.IsMatch(address, @"^0x[a-fA-F0-9]{40}$"))
        {
            errors.Add("Ethereum address must contain only hexadecimal characters after 0x");
            return false;
        }

        return true;
    }

    private static int GetDecimalPlaces(decimal value)
    {
        var text = value.ToString();
        var decimalIndex = text.IndexOf('.');
        return decimalIndex == -1 ? 0 : text.Length - decimalIndex - 1;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public string ErrorMessage => string.Join(", ", Errors);
}

public class CreateCryptoPaymentRequest
{
    public string PaymentId { get; set; } = "";
    public CryptoCurrency Currency { get; set; }
    public decimal Amount { get; set; }
    public string? BuyerId { get; set; }
    public int? ExpirationMinutes { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public enum CryptoCurrency
{
    Bitcoin = 1,
    Ethereum = 2,
    USDT = 3,
    USDC = 4
}

// Validation component for reuse in Blazor
public static class CryptoValidationHelpers
{
    public static string FormatAmount(decimal amount, CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => amount.ToString("F8"),
            CryptoCurrency.Ethereum => amount.ToString("F18").TrimEnd('0').TrimEnd('.'),
            CryptoCurrency.USDT or CryptoCurrency.USDC => amount.ToString("F6").TrimEnd('0').TrimEnd('.'),
            _ => amount.ToString("F8")
        };
    }

    public static string GetCurrencySymbol(CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => "BTC",
            CryptoCurrency.Ethereum => "ETH",
            CryptoCurrency.USDT => "USDT",
            CryptoCurrency.USDC => "USDC",
            _ => currency.ToString()
        };
    }

    public static string GetCurrencyName(CryptoCurrency currency)
    {
        return currency switch
        {
            CryptoCurrency.Bitcoin => "Bitcoin",
            CryptoCurrency.Ethereum => "Ethereum",
            CryptoCurrency.USDT => "Tether USD",
            CryptoCurrency.USDC => "USD Coin",
            _ => currency.ToString()
        };
    }

    public static bool IsAmountValid(decimal amount, CryptoCurrency currency, out string errorMessage)
    {
        var validator = new CryptoValidationService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<CryptoValidationService>());
        var result = validator.ValidatePaymentAmount(amount, currency);
        errorMessage = result.ErrorMessage;
        return result.IsValid;
    }

    public static bool IsAddressValid(string address, CryptoCurrency currency, out string errorMessage)
    {
        var validator = new CryptoValidationService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<CryptoValidationService>());
        var result = validator.ValidateCryptoAddress(address, currency);
        errorMessage = result.ErrorMessage;
        return result.IsValid;
    }
}