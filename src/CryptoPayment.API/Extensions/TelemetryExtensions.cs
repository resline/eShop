using Serilog;
using Serilog.Events;
using Serilog.Enrichers.Span;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace eShop.CryptoPayment.API.Extensions;

public static class TelemetryExtensions
{
    public static void AddTelemetryAndLogging(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        
        // Configure Serilog
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("ApplicationName", "CryptoPayment.API")
                .Enrich.WithSpan()
                .Enrich.WithCorrelationId()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    theme: AnsiConsoleTheme.Code)
                .Filter.ByExcluding("RequestPath like '/health%'")
                .Filter.ByExcluding("RequestPath like '/metrics%'");
                
            if (context.HostingEnvironment.IsProduction())
            {
                configuration
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning);
            }
            else
            {
                configuration.MinimumLevel.Debug();
            }
        });
        
        // Add OpenTelemetry
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("CryptoPayment.API", "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["service.instance.id"] = Environment.MachineName,
                    ["service.namespace"] = "eShop"
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddMeter(CryptoPaymentMetrics.MeterName)
                .AddPrometheusExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        activity.SetTag("http.request.correlation_id", request.HttpContext.TraceIdentifier);
                        if (request.HttpContext.User?.Identity?.Name != null)
                        {
                            activity.SetTag("user.id", request.HttpContext.User.Identity.Name);
                        }
                    };
                    options.EnrichWithHttpResponse = (activity, response) =>
                    {
                        activity.SetTag("http.response.status_code", response.StatusCode);
                    };
                })
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddRedisInstrumentation()
                .AddSource(CryptoPaymentMetrics.MeterName)
                .SetSampler(new TraceIdRatioBasedSampler(0.1)));
                
        // Add custom metrics
        services.AddSingleton<CryptoPaymentMetrics>();
        
        // Add structured logging context enrichers
        services.AddScoped<ILogContextEnricher, PaymentLogContextEnricher>();
        
        // Add correlation ID middleware
        services.AddTransient<CorrelationIdMiddleware>();
    }
}

public class CryptoPaymentMetrics
{
    public const string MeterName = "CryptoPayment.API";
    private readonly Meter _meter;
    
    public readonly Counter<long> PaymentsCreated;
    public readonly Counter<long> PaymentsCompleted;
    public readonly Counter<long> PaymentsFailed;
    public readonly Histogram<double> PaymentProcessingTime;
    public readonly Histogram<double> AddressGenerationTime;
    public readonly Counter<long> QRCodeGenerations;
    public readonly Counter<long> BlockchainConnections;
    public readonly Gauge<int> ActivePayments;
    public readonly Counter<long> WebhookDeliveries;
    public readonly Histogram<double> DatabaseOperationDuration;

    public CryptoPaymentMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        
        PaymentsCreated = _meter.CreateCounter<long>(
            "crypto_payments_created_total",
            "Total number of crypto payments created");
            
        PaymentsCompleted = _meter.CreateCounter<long>(
            "crypto_payments_completed_total", 
            "Total number of crypto payments completed successfully");
            
        PaymentsFailed = _meter.CreateCounter<long>(
            "crypto_payments_failed_total",
            "Total number of crypto payments that failed");
            
        PaymentProcessingTime = _meter.CreateHistogram<double>(
            "crypto_payment_processing_duration_seconds",
            "Time taken to process crypto payments",
            "seconds");
            
        AddressGenerationTime = _meter.CreateHistogram<double>(
            "crypto_address_generation_duration_seconds",
            "Time taken to generate crypto addresses",
            "seconds");
            
        QRCodeGenerations = _meter.CreateCounter<long>(
            "crypto_qr_codes_generated_total",
            "Total number of QR codes generated");
            
        BlockchainConnections = _meter.CreateCounter<long>(
            "blockchain_connections_total",
            "Total number of blockchain connections attempted");
            
        ActivePayments = _meter.CreateGauge<int>(
            "crypto_active_payments_current",
            "Current number of active crypto payments");
            
        WebhookDeliveries = _meter.CreateCounter<long>(
            "crypto_webhook_deliveries_total",
            "Total number of webhook deliveries attempted");
            
        DatabaseOperationDuration = _meter.CreateHistogram<double>(
            "crypto_database_operation_duration_seconds",
            "Time taken for database operations",
            "seconds");
    }
    
    public void RecordPaymentCreated(string currency, string buyerId)
    {
        PaymentsCreated.Add(1, new KeyValuePair<string, object?>[]
        {
            new("currency", currency),
            new("buyer_id", buyerId)
        });
    }
    
    public void RecordPaymentCompleted(string currency, double processingTimeSeconds)
    {
        PaymentsCompleted.Add(1, new KeyValuePair<string, object?>[]
        {
            new("currency", currency)
        });
        
        PaymentProcessingTime.Record(processingTimeSeconds, new KeyValuePair<string, object?>[]
        {
            new("currency", currency),
            new("status", "completed")
        });
    }
    
    public void RecordPaymentFailed(string currency, string reason, double processingTimeSeconds)
    {
        PaymentsFailed.Add(1, new KeyValuePair<string, object?>[]
        {
            new("currency", currency),
            new("reason", reason)
        });
        
        PaymentProcessingTime.Record(processingTimeSeconds, new KeyValuePair<string, object?>[]
        {
            new("currency", currency),
            new("status", "failed")
        });
    }
    
    public void RecordAddressGeneration(string currency, double generationTimeSeconds)
    {
        AddressGenerationTime.Record(generationTimeSeconds, new KeyValuePair<string, object?>[]
        {
            new("currency", currency)
        });
    }
    
    public void RecordQRCodeGeneration()
    {
        QRCodeGenerations.Add(1);
    }
    
    public void RecordBlockchainConnection(string network, bool success)
    {
        BlockchainConnections.Add(1, new KeyValuePair<string, object?>[]
        {
            new("network", network),
            new("success", success.ToString())
        });
    }
    
    public void UpdateActivePayments(int count)
    {
        ActivePayments.Record(count);
    }
    
    public void RecordWebhookDelivery(string eventType, bool success, double deliveryTimeSeconds)
    {
        WebhookDeliveries.Add(1, new KeyValuePair<string, object?>[]
        {
            new("event_type", eventType),
            new("success", success.ToString())
        });
    }
    
    public void RecordDatabaseOperation(string operation, double durationSeconds)
    {
        DatabaseOperationDuration.Record(durationSeconds, new KeyValuePair<string, object?>[]
        {
            new("operation", operation)
        });
    }
}

public interface ILogContextEnricher
{
    void EnrichContext(IDictionary<string, object> context);
}

public class PaymentLogContextEnricher : ILogContextEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PaymentLogContextEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void EnrichContext(IDictionary<string, object> context)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            context["RequestId"] = httpContext.TraceIdentifier;
            context["UserAgent"] = httpContext.Request.Headers.UserAgent.ToString();
            context["RequestPath"] = httpContext.Request.Path;
            context["RequestMethod"] = httpContext.Request.Method;
            
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                context["UserId"] = httpContext.User.Identity.Name ?? "unknown";
            }
            
            // Extract payment-specific context
            if (httpContext.Request.RouteValues.ContainsKey("paymentId"))
            {
                context["PaymentId"] = httpContext.Request.RouteValues["paymentId"];
            }
        }
    }
}

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        
        // Add correlation ID to response headers
        context.Response.Headers[CorrelationIdHeader] = correlationId;
        
        // Add to logging context
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });
        
        // Add to current activity
        if (Activity.Current != null)
        {
            Activity.Current.SetTag("correlation.id", correlationId);
        }
        
        await _next(context);
    }

    private string GetOrCreateCorrelationId(HttpContext context)
    {
        // Check if correlation ID exists in request headers
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId) && 
            !string.IsNullOrEmpty(correlationId))
        {
            return correlationId!;
        }
        
        // Use trace identifier as correlation ID
        return context.TraceIdentifier;
    }
}

public static class SerilogEnrichers
{
    public static LoggerConfiguration WithCorrelationId(this LoggerEnrichmentConfiguration enrichConfiguration)
    {
        return enrichConfiguration.With<CorrelationIdEnricher>();
    }
}

public class CorrelationIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = Activity.Current?.GetTagItem("correlation.id")?.ToString() ?? 
                           Thread.CurrentThread.ManagedThreadId.ToString();
                           
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
    }
}