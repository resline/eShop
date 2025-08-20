using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using CryptoPayment.API.Exceptions;

namespace CryptoPayment.API.Middleware;

public class ValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ValidationMiddleware> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ValidationMiddleware(
        RequestDelegate next,
        ILogger<ValidationMiddleware> logger,
        IServiceProvider serviceProvider)
    {
        _next = next;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only validate for POST, PUT, and PATCH requests
        if (!ShouldValidateRequest(context.Request))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Request.Body;
        
        try
        {
            // Read and validate the request body
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Reset the request body stream
            context.Request.Body = memoryStream;

            await _next(context);
        }
        catch (ValidationException validationEx)
        {
            await HandleValidationExceptionAsync(context, validationEx);
        }
        finally
        {
            context.Request.Body = originalBodyStream;
        }
    }

    private static bool ShouldValidateRequest(HttpRequest request)
    {
        var method = request.Method.ToUpperInvariant();
        return method is "POST" or "PUT" or "PATCH";
    }

    private async Task HandleValidationExceptionAsync(HttpContext context, ValidationException validationException)
    {
        _logger.LogInformation("Validation failed for {Path}: {Errors}", 
            context.Request.Path, 
            string.Join(", ", validationException.Errors.SelectMany(e => e.Value)));

        context.Response.Clear();
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";

        var problemDetails = new ValidationProblemDetails(validationException.Errors)
        {
            Type = "https://docs.cryptopayment.api/errors/validation",
            Title = "Validation Error",
            Status = 400,
            Detail = validationException.UserMessage,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["correlationId"] = GetCorrelationId(context);
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        var jsonResponse = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }

    private static string GetCorrelationId(HttpContext context)
    {
        return context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? 
               context.Response.Headers["X-Correlation-ID"].FirstOrDefault() ?? 
               Guid.NewGuid().ToString();
    }
}

// Extension for easy registration
public static class ValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseValidationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ValidationMiddleware>();
    }
}

// Enhanced FluentValidation extensions for better error handling
public static class FluentValidationExtensions
{
    public static IRuleBuilderOptions<T, TProperty> WithUserFriendlyMessage<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule, 
        string message)
    {
        return rule.WithMessage(message);
    }

    public static IRuleBuilderOptions<T, string> IsValidCryptoAddress<T>(
        this IRuleBuilder<T, string> ruleBuilder, 
        string currency)
    {
        return ruleBuilder
            .Must((address) => IsValidAddress(address, currency))
            .WithMessage($"Please provide a valid {currency} address")
            .WithErrorCode("INVALID_CRYPTO_ADDRESS");
    }

    public static IRuleBuilderOptions<T, decimal> IsPositiveAmount<T>(
        this IRuleBuilder<T, decimal> ruleBuilder)
    {
        return ruleBuilder
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero")
            .WithErrorCode("INVALID_AMOUNT");
    }

    public static IRuleBuilderOptions<T, decimal> IsWithinRange<T>(
        this IRuleBuilder<T, decimal> ruleBuilder, 
        decimal min, 
        decimal max, 
        string currency)
    {
        return ruleBuilder
            .InclusiveBetween(min, max)
            .WithMessage($"Amount must be between {min} and {max} {currency}")
            .WithErrorCode("AMOUNT_OUT_OF_RANGE");
    }

    public static IRuleBuilderOptions<T, string> IsSupportedCurrency<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        var supportedCurrencies = new[] { "BTC", "ETH", "LTC", "BCH", "XRP" };
        
        return ruleBuilder
            .Must(currency => supportedCurrencies.Contains(currency?.ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", supportedCurrencies)}")
            .WithErrorCode("UNSUPPORTED_CURRENCY");
    }

    private static bool IsValidAddress(string address, string currency)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        // Basic validation - in production, use proper address validation libraries
        return currency.ToUpperInvariant() switch
        {
            "BTC" => IsValidBitcoinAddress(address),
            "ETH" => IsValidEthereumAddress(address),
            "LTC" => IsValidLitecoinAddress(address),
            _ => address.Length > 20 && address.Length < 100 // Basic length check
        };
    }

    private static bool IsValidBitcoinAddress(string address)
    {
        // Simplified Bitcoin address validation
        if (string.IsNullOrWhiteSpace(address))
            return false;

        // Legacy addresses (P2PKH and P2SH)
        if (address.StartsWith('1') || address.StartsWith('3'))
        {
            return address.Length >= 26 && address.Length <= 35;
        }

        // Bech32 addresses (P2WPKH and P2WSH)
        if (address.StartsWith("bc1"))
        {
            return address.Length >= 42 && address.Length <= 62;
        }

        return false;
    }

    private static bool IsValidEthereumAddress(string address)
    {
        // Ethereum address validation
        if (string.IsNullOrWhiteSpace(address))
            return false;

        // Must start with 0x and be 42 characters long
        return address.StartsWith("0x") && address.Length == 42 &&
               address[2..].All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    private static bool IsValidLitecoinAddress(string address)
    {
        // Simplified Litecoin address validation
        if (string.IsNullOrWhiteSpace(address))
            return false;

        // Legacy addresses
        if (address.StartsWith('L') || address.StartsWith('M'))
        {
            return address.Length >= 26 && address.Length <= 34;
        }

        // Bech32 addresses
        if (address.StartsWith("ltc1"))
        {
            return address.Length >= 43 && address.Length <= 63;
        }

        return false;
    }
}