using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace eShop.CryptoPayment.API.Apis;

public static class CryptoPaymentApi
{
    public static IEndpointRouteBuilder MapCryptoPaymentApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/crypto").HasApiVersion(1.0);

        // Create crypto payment
        api.MapPost("/payment", CreatePaymentAsync)
            .WithName("CreateCryptoPayment")
            .WithSummary("Create a new crypto payment")
            .WithDescription("Creates a new cryptocurrency payment with a unique address")
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // Get payment status by external payment ID
        api.MapGet("/payment/{paymentId}", GetPaymentStatusAsync)
            .WithName("GetCryptoPaymentStatus")
            .WithSummary("Get payment status by external payment ID")
            .WithDescription("Retrieves the status of a cryptocurrency payment using external payment ID")
            .Produces<PaymentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        // Get payment by internal ID
        api.MapGet("/payment/internal/{id:int}", GetPaymentByIdAsync)
            .WithName("GetCryptoPaymentById")
            .WithSummary("Get payment by internal ID")
            .WithDescription("Retrieves a cryptocurrency payment using internal payment ID")
            .Produces<PaymentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        // Get payments by buyer ID
        api.MapGet("/payment/buyer/{buyerId}", GetPaymentsByBuyerAsync)
            .WithName("GetCryptoPaymentsByBuyer")
            .WithSummary("Get payments by buyer ID")
            .WithDescription("Retrieves all cryptocurrency payments for a specific buyer")
            .Produces<IEnumerable<PaymentResponse>>(StatusCodes.Status200OK);

        // Update payment status (internal use)
        api.MapPut("/payment/{id:int}/status", UpdatePaymentStatusAsync)
            .WithName("UpdateCryptoPaymentStatus")
            .WithSummary("Update payment status")
            .WithDescription("Updates the status of a cryptocurrency payment (internal use)")
            .Produces<PaymentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        // Exchange rate endpoints
        api.MapGet("/exchange-rates/{currency}", GetExchangeRateAsync)
            .WithName("GetExchangeRate")
            .WithSummary("Get current exchange rate for cryptocurrency")
            .WithDescription("Retrieves the current USD exchange rate for the specified cryptocurrency")
            .Produces<ExchangeRateResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        api.MapGet("/exchange-rates", GetExchangeRatesAsync)
            .WithName("GetExchangeRates")
            .WithSummary("Get current exchange rates for all supported cryptocurrencies")
            .WithDescription("Retrieves current USD exchange rates for all supported cryptocurrencies")
            .Produces<Dictionary<string, ExchangeRateResult>>(StatusCodes.Status200OK);

        // Webhook endpoint for payment providers
        api.MapPost("/webhook", ProcessWebhookAsync)
            .WithName("ProcessCryptoPaymentWebhook")
            .WithSummary("Process payment webhook")
            .WithDescription("Processes webhook notifications from payment providers")
            .Accepts<WebhookPayload>(MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .AllowAnonymous(); // Webhooks typically don't use authentication

        return app;
    }

    public static async Task<IResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        ICryptoPaymentService paymentService,
        ILogger<ICryptoPaymentService> logger)
    {
        try
        {
            logger.LogInformation("Creating crypto payment for PaymentId: {PaymentId}", request.PaymentId);

            var response = await paymentService.CreatePaymentAsync(request);
            return Results.Created($"/api/crypto/payment/{response.PaymentId}", response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid operation when creating payment for PaymentId: {PaymentId}", request.PaymentId);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid operation",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating crypto payment for PaymentId: {PaymentId}", request.PaymentId);
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while creating the payment",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> GetPaymentStatusAsync(
        string paymentId,
        ICryptoPaymentService paymentService,
        ILogger<ICryptoPaymentService> logger)
    {
        try
        {
            var payment = await paymentService.GetPaymentStatusAsync(paymentId);
            
            if (payment == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Payment not found",
                    Detail = $"Payment with ID '{paymentId}' was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Results.Ok(payment);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving payment status for PaymentId: {PaymentId}", paymentId);
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while retrieving the payment status",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> GetPaymentByIdAsync(
        int id,
        ICryptoPaymentService paymentService,
        ILogger<ICryptoPaymentService> logger)
    {
        try
        {
            var payment = await paymentService.GetPaymentByIdAsync(id);
            
            if (payment == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Payment not found",
                    Detail = $"Payment with ID {id} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Results.Ok(payment);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving payment by ID: {PaymentId}", id);
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while retrieving the payment",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> GetPaymentsByBuyerAsync(
        string buyerId,
        ICryptoPaymentService paymentService,
        ILogger<ICryptoPaymentService> logger)
    {
        try
        {
            var payments = await paymentService.GetPaymentsByBuyerIdAsync(buyerId);
            return Results.Ok(payments);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving payments for buyer: {BuyerId}", buyerId);
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while retrieving payments",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> UpdatePaymentStatusAsync(
        int id,
        UpdatePaymentStatusRequest request,
        ICryptoPaymentService paymentService,
        ILogger<ICryptoPaymentService> logger)
    {
        try
        {
            var payment = await paymentService.UpdatePaymentStatusAsync(
                id, request.Status, request.TransactionHash, request.ReceivedAmount, request.Confirmations);
            
            return Results.Ok(payment);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid operation when updating payment {PaymentId}", id);
            return Results.NotFound(new ProblemDetails
            {
                Title = "Payment not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating payment status for PaymentId: {PaymentId}", id);
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while updating the payment status",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> ProcessWebhookAsync(
        WebhookPayload webhookPayload,
        ICryptoPaymentService paymentService,
        ILogger<ICryptoPaymentService> logger)
    {
        try
        {
            logger.LogInformation("Processing webhook for payment: {PaymentId}, Event: {EventType}", 
                webhookPayload.Payment.PaymentId, webhookPayload.EventType);

            // TODO: Implement webhook signature verification
            // TODO: Process different event types based on webhookPayload.EventType

            switch (webhookPayload.EventType.ToLowerInvariant())
            {
                case "payment_received":
                case "payment_confirmed":
                    await paymentService.UpdatePaymentStatusAsync(
                        webhookPayload.Payment.Id,
                        webhookPayload.Payment.Status,
                        webhookPayload.Payment.TransactionHash,
                        webhookPayload.Payment.ReceivedAmount,
                        webhookPayload.Payment.Confirmations);
                    break;

                default:
                    logger.LogWarning("Unknown webhook event type: {EventType}", webhookPayload.EventType);
                    break;
            }

            return Results.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing webhook for payment: {PaymentId}", 
                webhookPayload.Payment?.PaymentId);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Webhook processing failed",
                Detail = "An error occurred while processing the webhook",
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    public static async Task<IResult> GetExchangeRateAsync(
        string currency,
        IExchangeRateService exchangeRateService,
        ILogger<IExchangeRateService> logger)
    {
        try
        {
            if (!Enum.TryParse<CryptoCurrencyType>(currency, true, out var cryptoCurrency))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid currency",
                    Detail = $"Currency '{currency}' is not supported",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var exchangeRate = await exchangeRateService.GetExchangeRateAsync(cryptoCurrency);
            return Results.Ok(exchangeRate);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Failed to get exchange rate for {Currency}", currency);
            return Results.NotFound(new ProblemDetails
            {
                Title = "Exchange rate not available",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting exchange rate for {Currency}", currency);
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while retrieving the exchange rate",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> GetExchangeRatesAsync(
        IExchangeRateService exchangeRateService,
        ILogger<IExchangeRateService> logger)
    {
        try
        {
            var currencies = Enum.GetValues<CryptoCurrencyType>();
            var exchangeRates = await exchangeRateService.GetExchangeRatesAsync(currencies);
            
            var result = exchangeRates.ToDictionary(
                kvp => kvp.Key.ToString(), 
                kvp => kvp.Value);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting exchange rates");
            return Results.Problem(
                title: "Internal server error",
                detail: "An error occurred while retrieving exchange rates",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class UpdatePaymentStatusRequest
{
    public PaymentStatus Status { get; set; }
    public string? TransactionHash { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public int? Confirmations { get; set; }
}