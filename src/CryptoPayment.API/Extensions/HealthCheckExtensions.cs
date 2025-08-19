using eShop.CryptoPayment.API.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace eShop.CryptoPayment.API.Extensions;

public static class HealthCheckExtensions
{
    public static void AddCryptoPaymentHealthChecks(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "database", "sql" })
            .AddCheck<RedisHealthCheck>(
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "cache", "redis" })
            .AddCheck<BlockchainHealthCheck>(
                name: "blockchain",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "blockchain", "external" })
            .AddCheck<ExchangeRateHealthCheck>(
                name: "exchange_rates",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "exchange", "external" })
            .AddCheck<EventBusHealthCheck>(
                name: "event_bus",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "messaging", "rabbitmq" });

        // Add health checks for critical dependencies
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: builder.Configuration.GetConnectionString("cryptopaymentdb")!,
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db" })
            .AddRabbitMQ(
                connectionString: builder.Configuration.GetConnectionString("eventbus")!,
                name: "rabbitmq",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "messaging" });

        // Add HTTP client health checks for external services
        services.AddHealthChecks()
            .AddUrlGroup(
                new Uri("https://api.coingecko.com/api/v3/ping"),
                name: "coingecko_api",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "external", "api" },
                timeout: TimeSpan.FromSeconds(5))
            .AddUrlGroup(
                new Uri("https://api.kraken.com/0/public/SystemStatus"),
                name: "kraken_api", 
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "external", "api" },
                timeout: TimeSpan.FromSeconds(5));

        // Configure health check options
        services.Configure<HealthCheckOptions>(options =>
        {
            options.AllowCachingResponses = true;
            options.ResultStatusCodes[HealthStatus.Healthy] = StatusCodes.Status200OK;
            options.ResultStatusCodes[HealthStatus.Degraded] = StatusCodes.Status200OK;
            options.ResultStatusCodes[HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable;
        });
    }

    public static void MapCryptoPaymentHealthChecks(this WebApplication app)
    {
        // Basic health check endpoint
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = HealthCheckResponseWriter.WriteResponse,
            AllowCachingResponses = false
        });

        // Readiness check (all services must be healthy)
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse,
            AllowCachingResponses = false
        });

        // Liveness check (basic application health)
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live") || check.Name == "database",
            ResponseWriter = HealthCheckResponseWriter.WriteSimpleResponse,
            AllowCachingResponses = false
        });

        // External dependencies check
        app.MapHealthChecks("/health/external", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("external"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse,
            AllowCachingResponses = false
        });

        // Database specific health check
        app.MapHealthChecks("/health/database", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("database") || check.Tags.Contains("db"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse,
            AllowCachingResponses = false
        });
    }
}

public static class HealthCheckResponseWriter
{
    public static async Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                duration = entry.Value.Duration.TotalMilliseconds,
                description = entry.Value.Description,
                data = entry.Value.Data.Count > 0 ? entry.Value.Data : null,
                exception = entry.Value.Exception?.Message,
                tags = entry.Value.Tags
            }).ToArray()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await context.Response.WriteAsync(json);
    }

    public static async Task WriteSimpleResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow
        };

        var json = System.Text.Json.JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

public class HealthCheckBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthCheckBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Timer _timer;

    public HealthCheckBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<HealthCheckBackgroundService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        
        var interval = _configuration.GetValue<int>("HealthCheck:BackgroundCheckIntervalSeconds", 60);
        _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(interval));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(60000, stoppingToken); // Background service loop
        }
    }

    private async void DoWork(object? state)
    {
        using var scope = _serviceProvider.CreateScope();
        var healthCheckService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
        
        try
        {
            var result = await healthCheckService.CheckHealthAsync();
            
            if (result.Status == HealthStatus.Unhealthy)
            {
                _logger.LogError("Health check failed with status: {Status}. Failed checks: {FailedChecks}",
                    result.Status,
                    string.Join(", ", result.Entries.Where(e => e.Value.Status == HealthStatus.Unhealthy).Select(e => e.Key)));
            }
            else if (result.Status == HealthStatus.Degraded)
            {
                _logger.LogWarning("Health check degraded with status: {Status}. Degraded checks: {DegradedChecks}",
                    result.Status,
                    string.Join(", ", result.Entries.Where(e => e.Value.Status == HealthStatus.Degraded).Select(e => e.Key)));
            }
            else
            {
                _logger.LogDebug("All health checks passed successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running background health checks");
        }
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}