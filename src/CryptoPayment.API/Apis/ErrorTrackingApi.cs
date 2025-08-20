using Microsoft.AspNetCore.Mvc;
using CryptoPayment.API.Services;

namespace CryptoPayment.API.Apis;

public static class ErrorTrackingApi
{
    public static IEndpointRouteBuilder MapErrorTrackingApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/error-tracking").HasApiVersion(1.0);

        api.MapPost("/client-errors", RecordClientError)
           .WithName("RecordClientError")
           .WithSummary("Record client-side error for tracking")
           .WithOpenApi();

        return app;
    }

    public static async Task<IResult> RecordClientError(
        [FromBody] ClientErrorReport errorReport,
        [FromServices] ILogger<ErrorTrackingApi> logger,
        [FromServices] IErrorHandlingService errorHandlingService,
        HttpContext context)
    {
        try
        {
            // Validate the error report
            if (string.IsNullOrEmpty(errorReport.ErrorType))
            {
                return Results.BadRequest("ErrorType is required");
            }

            // Extract additional context
            var clientIp = GetClientIpAddress(context);
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            
            // Log the client error with structured data
            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["ErrorSource"] = "Client",
                ["CorrelationId"] = errorReport.CorrelationId ?? Guid.NewGuid().ToString(),
                ["TransactionId"] = errorReport.TransactionId ?? "N/A",
                ["ClientIp"] = clientIp,
                ["UserAgent"] = userAgent,
                ["Component"] = errorReport.Component ?? "Unknown",
                ["Url"] = errorReport.Url ?? "Unknown"
            });

            logger.LogError("Client error reported: {ErrorType} - {ErrorMessage}. StackTrace: {StackTrace}",
                errorReport.ErrorType,
                errorReport.ErrorMessage,
                errorReport.StackTrace);

            // Record metrics for monitoring
            if (!string.IsNullOrEmpty(errorReport.CorrelationId))
            {
                await errorHandlingService.RecordErrorMetricsAsync(
                    new Exception($"Client Error: {errorReport.ErrorType} - {errorReport.ErrorMessage}"),
                    errorReport.CorrelationId,
                    context.User?.Identity?.Name);
            }

            return Results.Ok(new { 
                Status = "Recorded", 
                CorrelationId = errorReport.CorrelationId ?? Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record client error");
            return Results.Problem("Failed to record error", statusCode: 500);
        }
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers first
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

public record ClientErrorReport
{
    public string? CorrelationId { get; init; }
    public string? TransactionId { get; init; }
    public string ErrorType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string? StackTrace { get; init; }
    public string? UserAgent { get; init; }
    public string? Url { get; init; }
    public string? Component { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}