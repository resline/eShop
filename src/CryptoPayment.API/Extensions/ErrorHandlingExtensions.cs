using FluentValidation;
using CryptoPayment.API.Services;
using CryptoPayment.API.Middleware;
using Microsoft.Extensions.Localization;

namespace CryptoPayment.API.Extensions;

public static class ErrorHandlingExtensions
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        // Core error handling services
        services.AddScoped<IErrorHandlingService, ErrorHandlingService>();
        services.AddScoped<IErrorRecoveryService, ErrorRecoveryService>();
        
        // Add FluentValidation
        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddFluentValidationAutoValidation();
        services.AddFluentValidationClientsideAdapters();
        
        // Add localization for error messages
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        
        // Add problem details
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = (context) =>
            {
                // Add correlation ID to all problem details
                var correlationId = context.HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault() ??
                                   context.HttpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault() ??
                                   Guid.NewGuid().ToString();
                
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
                context.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });

        return services;
    }

    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app)
    {
        // Use global exception middleware first
        app.UseGlobalExceptionHandling();
        
        // Use validation middleware
        app.UseValidationMiddleware();
        
        return app;
    }

    public static IServiceCollection AddFluentValidationAutoValidation(this IServiceCollection services)
    {
        services.Configure<FluentValidation.AspNetCore.FluentValidationMvcConfiguration>(config =>
        {
            config.DisableDataAnnotationsValidation = false;
            config.ValidatorOptions.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
            config.ValidatorOptions.DefaultClassLevelCascadeMode = CascadeMode.Stop;
        });

        return services;
    }

    public static IServiceCollection AddFluentValidationClientsideAdapters(this IServiceCollection services)
    {
        // Add client-side validation adapters for better UX
        services.AddScoped<IClientValidationRuleFactory, DefaultClientValidationRuleFactory>();
        
        return services;
    }
}

// Default client validation rule factory
public interface IClientValidationRuleFactory
{
    IEnumerable<ClientValidationRule> CreateRules(Type validatorType, string propertyName);
}

public class DefaultClientValidationRuleFactory : IClientValidationRuleFactory
{
    public IEnumerable<ClientValidationRule> CreateRules(Type validatorType, string propertyName)
    {
        // Implementation for creating client-side validation rules
        // This would integrate with your frontend validation system
        return Enumerable.Empty<ClientValidationRule>();
    }
}

public record ClientValidationRule
{
    public string ValidationType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public Dictionary<string, object> ValidationParameters { get; init; } = new();
}