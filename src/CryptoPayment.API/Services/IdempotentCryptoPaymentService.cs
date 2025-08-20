using eShop.CryptoPayment.API.Models;

namespace eShop.CryptoPayment.API.Services;

// Enhanced crypto payment service with idempotency support
public class IdempotentCryptoPaymentService : ICryptoPaymentService
{
    private readonly ICryptoPaymentService _baseService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<IdempotentCryptoPaymentService> _logger;

    public IdempotentCryptoPaymentService(
        ICryptoPaymentService baseService,
        IIdempotencyService idempotencyService,
        ILogger<IdempotentCryptoPaymentService> logger)
    {
        _baseService = baseService;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    public async Task<CryptoPayment> CreatePaymentAsync(CreateCryptoPaymentRequest request, CancellationToken cancellationToken = default)
    {
        // Generate idempotency key if not provided
        var idempotencyKey = request.IdempotencyKey ?? await _idempotencyService.GenerateIdempotencyKeyAsync(request);
        
        _logger.LogInformation("Creating payment with idempotency key {IdempotencyKey}", idempotencyKey);

        var result = await _idempotencyService.ExecuteIdempotentAsync(
            idempotencyKey,
            async () =>
            {
                _logger.LogInformation("Executing payment creation for amount {Amount} {Currency} (Order: {OrderId})",
                    request.Amount, request.Currency, request.OrderId);
                    
                var payment = await _baseService.CreatePaymentAsync(request, cancellationToken);
                
                _logger.LogInformation("Payment created successfully: {PaymentId}", payment.PaymentId);
                return payment;
            },
            TimeSpan.FromHours(2), // Cache payment creation for 2 hours
            cancellationToken);

        if (result.WasCached)
        {
            _logger.LogInformation("Payment creation was deduplicated using cached result for key {IdempotencyKey}", idempotencyKey);
        }

        return result.Value;
    }

    public async Task<CryptoPayment> UpdatePaymentStatusAsync(string paymentId, PaymentStatus status, string? transactionHash = null, CancellationToken cancellationToken = default)
    {
        // Generate idempotency key for status updates
        var idempotencyKey = $"payment_status_update_{paymentId}_{status}_{transactionHash ?? "none"}";
        
        var result = await _idempotencyService.ExecuteIdempotentAsync(
            idempotencyKey,
            async () =>
            {
                _logger.LogInformation("Updating payment {PaymentId} status to {Status}", paymentId, status);
                return await _baseService.UpdatePaymentStatusAsync(paymentId, status, transactionHash, cancellationToken);
            },
            TimeSpan.FromMinutes(30), // Cache status updates for 30 minutes
            cancellationToken);

        return result.Value;
    }

    public async Task<CryptoPayment> ProcessConfirmationAsync(string paymentId, string transactionHash, decimal receivedAmount, int confirmations, CancellationToken cancellationToken = default)
    {
        // Generate idempotency key for confirmations
        var idempotencyKey = $"payment_confirmation_{paymentId}_{transactionHash}_{confirmations}";
        
        var result = await _idempotencyService.ExecuteIdempotentAsync(
            idempotencyKey,
            async () =>
            {
                _logger.LogInformation("Processing confirmation for payment {PaymentId} with {Confirmations} confirmations", 
                    paymentId, confirmations);
                return await _baseService.ProcessConfirmationAsync(paymentId, transactionHash, receivedAmount, confirmations, cancellationToken);
            },
            TimeSpan.FromHours(1), // Cache confirmations for 1 hour
            cancellationToken);

        return result.Value;
    }

    // Delegate other methods without idempotency (read operations)
    public Task<CryptoPayment?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
        => _baseService.GetPaymentAsync(paymentId, cancellationToken);

    public Task<CryptoPayment?> GetPaymentByTransactionHashAsync(string transactionHash, CancellationToken cancellationToken = default)
        => _baseService.GetPaymentByTransactionHashAsync(transactionHash, cancellationToken);

    public Task<IEnumerable<CryptoPayment>> GetPaymentsByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
        => _baseService.GetPaymentsByOrderIdAsync(orderId, cancellationToken);

    public Task<IEnumerable<CryptoPayment>> GetPaymentsByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        => _baseService.GetPaymentsByUserIdAsync(userId, cancellationToken);

    public Task<bool> CanUserAccessPaymentAsync(string paymentId, string userId, CancellationToken cancellationToken = default)
        => _baseService.CanUserAccessPaymentAsync(paymentId, userId, cancellationToken);

    public Task<bool> IsPaymentExpiredAsync(string paymentId, CancellationToken cancellationToken = default)
        => _baseService.IsPaymentExpiredAsync(paymentId, cancellationToken);

    public Task<PaymentStatistics> GetPaymentStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
        => _baseService.GetPaymentStatisticsAsync(fromDate, toDate, cancellationToken);
}

// Enhanced create payment request with idempotency support
public class CreateCryptoPaymentRequest
{
    public string OrderId { get; set; } = "";
    public string UserId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public CryptoCurrencyType CryptoCurrency { get; set; }
    public DateTime ExpirationTime { get; set; }
    public string? CallbackUrl { get; set; }
    public string? IdempotencyKey { get; set; } // Optional client-provided key
    public Dictionary<string, string> Metadata { get; set; } = new();
}

// Payment deduplication tracking
public class PaymentDeduplicationTracker
{
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<PaymentDeduplicationTracker> _logger;

    public PaymentDeduplicationTracker(
        IIdempotencyService idempotencyService,
        ILogger<PaymentDeduplicationTracker> logger)
    {
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    public async Task<bool> IsDuplicatePaymentRequestAsync(CreateCryptoPaymentRequest request)
    {
        // Check for duplicate based on order ID and amount
        var duplicateCheckKey = $"payment_check_{request.OrderId}_{request.Amount}_{request.CryptoCurrency}";
        return await _idempotencyService.IsIdempotencyKeyUsedAsync(duplicateCheckKey);
    }

    public async Task<bool> IsDuplicateTransactionAsync(string transactionHash, string paymentId)
    {
        // Check for duplicate transaction processing
        var duplicateCheckKey = $"transaction_check_{transactionHash}_{paymentId}";
        return await _idempotencyService.IsIdempotencyKeyUsedAsync(duplicateCheckKey);
    }

    public async Task MarkTransactionProcessedAsync(string transactionHash, string paymentId)
    {
        var key = $"transaction_check_{transactionHash}_{paymentId}";
        await _idempotencyService.ExecuteIdempotentAsync(
            key,
            () => Task.FromResult(true),
            TimeSpan.FromDays(1)); // Keep transaction tracking for 1 day
    }
}

// Idempotency extension methods
public static class IdempotencyExtensions
{
    public static string GetIdempotencyKey(this HttpContext context)
    {
        return context.Items["IdempotencyKey"] as string ?? "";
    }

    public static void SetIdempotencyKey(this HttpContext context, string key)
    {
        context.Items["IdempotencyKey"] = key;
    }

    public static async Task<T> ExecuteIdempotentAsync<T>(this ControllerBase controller, 
        Func<Task<T>> operation,
        string? customKey = null,
        TimeSpan? expiry = null)
    {
        var idempotencyService = controller.HttpContext.RequestServices.GetRequiredService<IIdempotencyService>();
        
        var idempotencyKey = customKey ?? 
                           controller.HttpContext.GetIdempotencyKey() ?? 
                           await idempotencyService.GenerateIdempotencyKeyAsync(new {
                               controller.Request.Path,
                               controller.Request.Method,
                               Timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmm")
                           });

        var result = await idempotencyService.ExecuteIdempotentAsync(
            idempotencyKey,
            operation,
            expiry);

        // Add idempotency headers to response
        controller.Response.Headers["X-Idempotency-Key"] = idempotencyKey;
        controller.Response.Headers["X-Idempotency-Cached"] = result.WasCached.ToString();
        
        return result.Value;
    }
}

// Payment request validation with deduplication
public class PaymentRequestValidator
{
    private readonly PaymentDeduplicationTracker _deduplicationTracker;
    private readonly ILogger<PaymentRequestValidator> _logger;

    public PaymentRequestValidator(
        PaymentDeduplicationTracker deduplicationTracker,
        ILogger<PaymentRequestValidator> logger)
    {
        _deduplicationTracker = deduplicationTracker;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateCreatePaymentRequestAsync(CreateCryptoPaymentRequest request)
    {
        var errors = new List<string>();

        // Basic validation
        if (string.IsNullOrEmpty(request.OrderId))
            errors.Add("Order ID is required");
            
        if (string.IsNullOrEmpty(request.UserId))
            errors.Add("User ID is required");
            
        if (request.Amount <= 0)
            errors.Add("Amount must be greater than zero");
            
        if (request.ExpirationTime <= DateTime.UtcNow)
            errors.Add("Expiration time must be in the future");

        // Check for duplicates
        if (await _deduplicationTracker.IsDuplicatePaymentRequestAsync(request))
        {
            errors.Add("Duplicate payment request detected");
            _logger.LogWarning("Duplicate payment request detected for Order: {OrderId}, Amount: {Amount}", 
                request.OrderId, request.Amount);
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

// Background service to clean up old idempotency keys
public class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IdempotencyCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6);

    public IdempotencyCleanupService(
        IServiceProvider serviceProvider,
        ILogger<IdempotencyCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
                
                using var scope = _serviceProvider.CreateScope();
                var idempotencyService = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
                
                var metrics = await idempotencyService.GetMetricsAsync();
                _logger.LogInformation("Idempotency metrics - Total: {Total}, Cache Hits: {Hits}, Hit Ratio: {Ratio:P2}", 
                    metrics.TotalRequests, metrics.CacheHits, metrics.CacheHitRatio);
                
                // Additional cleanup logic could be added here
                // For example, removing expired keys from persistent storage
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during idempotency cleanup");
            }
        }
    }
}