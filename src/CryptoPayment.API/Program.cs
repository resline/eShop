using eShop.CryptoPayment.API.Apis;
using CryptoPayment.API.Apis;
using eShop.CryptoPayment.API.Hubs;
using CryptoPayment.API.Middleware;
using CryptoPayment.API.Services;
using CryptoPayment.API.Extensions;
using CryptoPayment.BlockchainServices.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();

// Add comprehensive error handling
builder.Services.AddErrorHandling();

// Add security services
builder.Services.AddKeyVaultConfiguration();
builder.Services.AddWebhookValidation();
builder.Services.AddCryptoPaymentRateLimiting();

var withApiVersioning = builder.Services.AddApiVersioning();

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure SignalR Hub
app.MapHub<PaymentStatusHub>("/hubs/payment-status");

// Configure CORS for SignalR
app.UseCors(policy => policy
    .WithOrigins("https://localhost:7240", "http://localhost:5173") // WebApp URLs
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials());

// Add error handling middleware (before other middleware)
app.UseErrorHandling();

// Add security middleware
app.UseRateLimiter();
app.UseMiddleware<RateLimitingLoggingMiddleware>();

var cryptoPayment = app.NewVersionedApi("CryptoPayment");

cryptoPayment.MapCryptoPaymentApiV1()
           .RequireAuthorization();

// Map error tracking API (no authorization required for error reporting)
app.MapErrorTrackingApiV1();

app.UseDefaultOpenApi();
app.Run();