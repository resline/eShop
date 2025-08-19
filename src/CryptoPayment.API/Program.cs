using eShop.CryptoPayment.API.Apis;
using eShop.CryptoPayment.API.Hubs;
using CryptoPayment.API.Middleware;
using CryptoPayment.API.Services;
using CryptoPayment.BlockchainServices.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();

// Add security services
builder.Services.AddKeyVaultConfiguration();
builder.Services.AddWebhookValidation();
builder.Services.AddCryptoPaymentRateLimiting();

builder.Services.AddProblemDetails();

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

// Add security middleware
app.UseRateLimiter();
app.UseMiddleware<RateLimitingLoggingMiddleware>();

var cryptoPayment = app.NewVersionedApi("CryptoPayment");

cryptoPayment.MapCryptoPaymentApiV1()
           .RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();