using FluentValidation;
using System.Text.RegularExpressions;

namespace eShop.CryptoPayment.API.Validation;

public class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    private static readonly Dictionary<CryptoCurrencyType, (decimal Min, decimal Max)> AmountLimits = new()
    {
        { CryptoCurrencyType.Bitcoin, (0.00000001m, 10m) },     // 1 satoshi to 10 BTC
        { CryptoCurrencyType.Ethereum, (0.000000000000000001m, 100m) } // 1 wei to 100 ETH
    };

    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .WithMessage("Payment ID is required")
            .Length(1, 100)
            .WithMessage("Payment ID must be between 1 and 100 characters")
            .Matches(@"^[a-zA-Z0-9\-_]+$")
            .WithMessage("Payment ID can only contain alphanumeric characters, hyphens, and underscores");

        RuleFor(x => x.CryptoCurrency)
            .IsInEnum()
            .WithMessage("Invalid cryptocurrency type");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than 0")
            .Must((request, amount) => IsAmountWithinLimits(request.CryptoCurrency, amount))
            .WithMessage((request, amount) => GetAmountLimitMessage(request.CryptoCurrency));

        RuleFor(x => x.BuyerId)
            .Length(1, 100)
            .WithMessage("Buyer ID must be between 1 and 100 characters")
            .Matches(@"^[a-zA-Z0-9\-_@.]+$")
            .WithMessage("Buyer ID contains invalid characters")
            .When(x => !string.IsNullOrEmpty(x.BuyerId));

        RuleFor(x => x.ExpirationMinutes)
            .InclusiveBetween(5, 1440)
            .WithMessage("Expiration must be between 5 minutes and 24 hours")
            .When(x => x.ExpirationMinutes.HasValue);

        RuleFor(x => x.Metadata)
            .Must(BeValidMetadata)
            .WithMessage("Metadata contains invalid values or is too large")
            .When(x => x.Metadata != null);
    }

    private static bool IsAmountWithinLimits(CryptoCurrencyType currency, decimal amount)
    {
        if (AmountLimits.TryGetValue(currency, out var limits))
        {
            return amount >= limits.Min && amount <= limits.Max;
        }
        return amount > 0; // Default validation
    }

    private static string GetAmountLimitMessage(CryptoCurrencyType currency)
    {
        if (AmountLimits.TryGetValue(currency, out var limits))
        {
            return $"Amount must be between {limits.Min} and {limits.Max} {currency}";
        }
        return "Amount is outside acceptable limits";
    }

    private static bool BeValidMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null) return true;

        // Check total number of entries
        if (metadata.Count > 50) return false;

        foreach (var kvp in metadata)
        {
            // Check key length and format
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Key.Length > 100) return false;
            if (!Regex.IsMatch(kvp.Key, @"^[a-zA-Z0-9_\-\.]+$")) return false;

            // Check value - must be serializable primitive types
            if (kvp.Value == null) continue;
            
            var valueType = kvp.Value.GetType();
            if (!IsSupportedMetadataType(valueType)) return false;

            // Check string length
            if (kvp.Value is string str && str.Length > 1000) return false;
        }

        return true;
    }

    private static bool IsSupportedMetadataType(Type type)
    {
        return type == typeof(string) ||
               type == typeof(int) ||
               type == typeof(long) ||
               type == typeof(decimal) ||
               type == typeof(double) ||
               type == typeof(bool) ||
               type == typeof(DateTime) ||
               type == typeof(Guid);
    }
}

public class UpdatePaymentStatusRequestValidator : AbstractValidator<UpdatePaymentStatusRequest>
{
    public UpdatePaymentStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Invalid payment status");

        RuleFor(x => x.TransactionHash)
            .Length(1, 200)
            .WithMessage("Transaction hash must be between 1 and 200 characters")
            .Must(BeValidTransactionHash)
            .WithMessage("Invalid transaction hash format")
            .When(x => !string.IsNullOrEmpty(x.TransactionHash));

        RuleFor(x => x.ReceivedAmount)
            .GreaterThan(0)
            .WithMessage("Received amount must be greater than 0")
            .When(x => x.ReceivedAmount.HasValue);

        RuleFor(x => x.Confirmations)
            .InclusiveBetween(0, 1000)
            .WithMessage("Confirmations must be between 0 and 1000")
            .When(x => x.Confirmations.HasValue);
    }

    private static bool BeValidTransactionHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return true;

        // Bitcoin transaction hash: 64 hexadecimal characters
        if (Regex.IsMatch(hash, @"^[a-fA-F0-9]{64}$")) return true;

        // Ethereum transaction hash: 0x followed by 64 hexadecimal characters
        if (Regex.IsMatch(hash, @"^0x[a-fA-F0-9]{64}$")) return true;

        return false;
    }
}

public class PaymentAddressValidator : AbstractValidator<string>
{
    public PaymentAddressValidator()
    {
        RuleFor(address => address)
            .NotEmpty()
            .WithMessage("Payment address is required")
            .Must(BeValidCryptoAddress)
            .WithMessage("Invalid cryptocurrency address format");
    }

    private static bool BeValidCryptoAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;

        // Bitcoin address patterns
        if (IsValidBitcoinAddress(address)) return true;

        // Ethereum address pattern
        if (IsValidEthereumAddress(address)) return true;

        return false;
    }

    private static bool IsValidBitcoinAddress(string address)
    {
        // Legacy addresses (starts with 1)
        if (address.StartsWith("1") && address.Length >= 26 && address.Length <= 35)
        {
            return Regex.IsMatch(address, @"^[1][a-km-zA-HJ-NP-Z1-9]{25,34}$");
        }

        // Script hash addresses (starts with 3)
        if (address.StartsWith("3") && address.Length >= 26 && address.Length <= 35)
        {
            return Regex.IsMatch(address, @"^[3][a-km-zA-HJ-NP-Z1-9]{25,34}$");
        }

        // Bech32 addresses (starts with bc1)
        if (address.StartsWith("bc1"))
        {
            return Regex.IsMatch(address, @"^bc1[a-z0-9]{39,59}$");
        }

        return false;
    }

    private static bool IsValidEthereumAddress(string address)
    {
        // Ethereum addresses: 0x followed by 40 hexadecimal characters
        return Regex.IsMatch(address, @"^0x[a-fA-F0-9]{40}$");
    }
}

public class CryptoCurrencyAmountValidator : AbstractValidator<CryptoCurrencyAmountRequest>
{
    public CryptoCurrencyAmountValidator()
    {
        RuleFor(x => x.Currency)
            .IsInEnum()
            .WithMessage("Invalid cryptocurrency type");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than 0");

        RuleFor(x => x)
            .Must(x => HasValidPrecision(x.Currency, x.Amount))
            .WithMessage(x => GetPrecisionMessage(x.Currency));
    }

    private static bool HasValidPrecision(CryptoCurrencyType currency, decimal amount)
    {
        var maxDecimals = currency switch
        {
            CryptoCurrencyType.Bitcoin => 8,  // Bitcoin has 8 decimal places (satoshis)
            CryptoCurrencyType.Ethereum => 18, // Ethereum has 18 decimal places (wei)
            _ => 8 // Default
        };

        var decimalPlaces = GetDecimalPlaces(amount);
        return decimalPlaces <= maxDecimals;
    }

    private static int GetDecimalPlaces(decimal value)
    {
        var text = value.ToString();
        var decimalIndex = text.IndexOf('.');
        return decimalIndex == -1 ? 0 : text.Length - decimalIndex - 1;
    }

    private static string GetPrecisionMessage(CryptoCurrencyType currency)
    {
        var maxDecimals = currency switch
        {
            CryptoCurrencyType.Bitcoin => 8,
            CryptoCurrencyType.Ethereum => 18,
            _ => 8
        };

        return $"Amount has too many decimal places. {currency} supports up to {maxDecimals} decimal places.";
    }
}

// Request DTOs for validation
public class UpdatePaymentStatusRequest
{
    public PaymentStatus Status { get; set; }
    public string? TransactionHash { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public int? Confirmations { get; set; }
}

public class CryptoCurrencyAmountRequest
{
    public CryptoCurrencyType Currency { get; set; }
    public decimal Amount { get; set; }
}

// Custom validation attributes
public class ValidCryptoAddressAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string address) return false;
        
        var validator = new PaymentAddressValidator();
        var result = validator.Validate(address);
        return result.IsValid;
    }

    public override string FormatErrorMessage(string name)
    {
        return $"{name} is not a valid cryptocurrency address.";
    }
}

public class ValidAmountForCurrencyAttribute : ValidationAttribute
{
    public CryptoCurrencyType Currency { get; set; }

    public ValidAmountForCurrencyAttribute(CryptoCurrencyType currency)
    {
        Currency = currency;
    }

    public override bool IsValid(object? value)
    {
        if (value is not decimal amount) return false;

        var request = new CryptoCurrencyAmountRequest { Currency = Currency, Amount = amount };
        var validator = new CryptoCurrencyAmountValidator();
        var result = validator.Validate(request);
        return result.IsValid;
    }

    public override string FormatErrorMessage(string name)
    {
        return $"{name} is not valid for {Currency}.";
    }
}

// Validation middleware
public class ValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ValidationMiddleware> _logger;

    public ValidationMiddleware(RequestDelegate next, ILogger<ValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add request validation logging
        if (context.Request.Method != "GET" && context.Request.ContentLength > 0)
        {
            context.Request.EnableBuffering();
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            context.Request.Body.Position = 0;

            _logger.LogDebug("Request validation for {Method} {Path}: {Body}", 
                context.Request.Method, context.Request.Path, body);
        }

        await _next(context);
    }
}

// Validation service extensions
public static class ValidationExtensions
{
    public static void AddCryptoPaymentValidation(this IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation();
        services.AddFluentValidationClientsideAdapters();
        services.AddValidatorsFromAssemblyContaining<CreatePaymentRequestValidator>();
        
        // Configure validation behavior
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .SelectMany(e => e.Value!.Errors.Select(er => new
                    {
                        Field = e.Key,
                        Error = er.ErrorMessage
                    }))
                    .ToArray();

                var response = new
                {
                    Title = "Validation failed",
                    Status = 400,
                    Errors = errors,
                    TraceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier
                };

                return new BadRequestObjectResult(response);
            };
        });
    }

    public static bool IsValidCryptoAddress(this string address, CryptoCurrencyType currency)
    {
        var validator = new PaymentAddressValidator();
        var result = validator.Validate(address);
        
        if (!result.IsValid) return false;

        // Additional currency-specific validation
        return currency switch
        {
            CryptoCurrencyType.Bitcoin => address.StartsWith("1") || address.StartsWith("3") || address.StartsWith("bc1"),
            CryptoCurrencyType.Ethereum => address.StartsWith("0x") && address.Length == 42,
            _ => true
        };
    }

    public static ValidationResult ValidateAmount(this decimal amount, CryptoCurrencyType currency)
    {
        var request = new CryptoCurrencyAmountRequest { Currency = currency, Amount = amount };
        var validator = new CryptoCurrencyAmountValidator();
        return validator.Validate(request);
    }
}