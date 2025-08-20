using System.Text.Json;
using System.Diagnostics;
using eShop.CryptoPayment.API.Extensions;

namespace eShop.CryptoPayment.API.Services;

public class CryptoPaymentServiceEnhanced : ICryptoPaymentService
{
    private readonly CryptoPaymentContext _context;
    private readonly IAddressGenerationService _addressService;
    private readonly ICryptoPaymentIntegrationEventService _integrationEventService;
    private readonly ILogger<CryptoPaymentServiceEnhanced> _logger;
    private readonly CryptoPaymentMetrics _metrics;
    private readonly ILogContextEnricher _logContextEnricher;
    private readonly ActivitySource _activitySource;

    public CryptoPaymentServiceEnhanced(
        CryptoPaymentContext context,
        IAddressGenerationService addressService,
        ICryptoPaymentIntegrationEventService integrationEventService,
        ILogger<CryptoPaymentServiceEnhanced> logger,
        CryptoPaymentMetrics metrics,
        ILogContextEnricher logContextEnricher)
    {
        _context = context;
        _addressService = addressService;
        _integrationEventService = integrationEventService;
        _logger = logger;
        _metrics = metrics;
        _logContextEnricher = logContextEnricher;
        _activitySource = new ActivitySource(CryptoPaymentMetrics.MeterName);
    }

    public async Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("CreatePayment");
        var stopwatch = Stopwatch.StartNew();
        
        var logContext = new Dictionary<string, object>
        {
            ["PaymentId"] = request.PaymentId,
            ["CryptoCurrency"] = request.CryptoCurrency.ToString(),
            ["Amount"] = request.Amount,
            ["BuyerId"] = request.BuyerId ?? "anonymous"
        };
        _logContextEnricher.EnrichContext(logContext);
        
        activity?.SetTag("payment.id", request.PaymentId);
        activity?.SetTag("payment.currency", request.CryptoCurrency.ToString());
        activity?.SetTag("payment.amount", request.Amount.ToString());
        activity?.SetTag("payment.buyer_id", request.BuyerId ?? "anonymous");

        using var logScope = _logger.BeginScope(logContext);
        
        _logger.LogInformation("Creating crypto payment for PaymentId: {PaymentId}, Currency: {CryptoCurrency}, Amount: {Amount}",
            request.PaymentId, request.CryptoCurrency, request.Amount);

        try
        {
            // Check if payment already exists
            var existingPayment = await _context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .FirstOrDefaultAsync(p => p.PaymentId == request.PaymentId, cancellationToken);

            if (existingPayment != null)
            {
                _logger.LogWarning("Payment already exists for PaymentId: {PaymentId}. Returning existing payment.", request.PaymentId);
                activity?.SetTag("payment.already_exists", true);
                return MapToPaymentResponse(existingPayment);
            }

            // Record database operation timing
            var dbStopwatch = Stopwatch.StartNew();
            
            // Get or generate payment address
            var paymentAddress = await _addressService.GetUnusedAddressAsync(request.CryptoCurrency, cancellationToken)
                ?? await _addressService.GenerateAddressAsync(request.CryptoCurrency, cancellationToken);

            _logger.LogInformation("Generated/retrieved payment address for currency: {CryptoCurrency}",
                request.CryptoCurrency);

            // Get cryptocurrency info
            var cryptoCurrency = await _context.CryptoCurrencies
                .FirstOrDefaultAsync(c => c.Id == (int)request.CryptoCurrency, cancellationToken);
                
            if (cryptoCurrency == null)
            {
                var errorMessage = $"Cryptocurrency {request.CryptoCurrency} not found";
                _logger.LogError("Cryptocurrency not found: {CryptoCurrency}", request.CryptoCurrency);
                activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
                _metrics.RecordPaymentFailed(request.CryptoCurrency.ToString(), "currency_not_found", stopwatch.Elapsed.TotalSeconds);
                throw new InvalidOperationException(errorMessage);
            }

            // Create payment
            var payment = new CryptoPayment
            {
                PaymentId = request.PaymentId,
                CryptoCurrencyId = (int)request.CryptoCurrency,
                PaymentAddressId = paymentAddress.Id,
                RequestedAmount = request.Amount,
                Status = PaymentStatus.Pending,
                RequiredConfirmations = request.CryptoCurrency == CryptoCurrencyType.Bitcoin ? 6 : 12,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpirationMinutes.HasValue 
                    ? DateTime.UtcNow.AddMinutes(request.ExpirationMinutes.Value) 
                    : DateTime.UtcNow.AddMinutes(30),
                BuyerId = request.BuyerId,
                Metadata = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null
            };

            _context.CryptoPayments.Add(payment);

            // Mark address as used
            await _addressService.MarkAddressAsUsedAsync(paymentAddress.Id, cancellationToken);

            // Save and publish integration event
            await _context.SaveChangesAsync(cancellationToken);
            
            dbStopwatch.Stop();
            _metrics.RecordDatabaseOperation("create_payment", dbStopwatch.Elapsed.TotalSeconds);

            // Reload with navigation properties
            await _context.Entry(payment)
                .Reference(p => p.CryptoCurrency)
                .LoadAsync(cancellationToken);
            await _context.Entry(payment)
                .Reference(p => p.PaymentAddress)
                .LoadAsync(cancellationToken);

            // Publish integration event
            var integrationEvent = new CryptoPaymentCreatedIntegrationEvent(
                payment.Id, payment.PaymentId, payment.CryptoCurrency.Symbol, 
                payment.PaymentAddress.Address, payment.RequestedAmount);
            
            await _integrationEventService.PublishEventsThroughEventBusAsync(integrationEvent);

            stopwatch.Stop();
            _metrics.RecordPaymentCreated(request.CryptoCurrency.ToString(), request.BuyerId ?? "anonymous");
            
            _logger.LogInformation("Successfully created crypto payment with ID: {PaymentDbId} for PaymentId: {PaymentId} in {ElapsedMs}ms",
                payment.Id, payment.PaymentId, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("payment.db_id", payment.Id);
            activity?.SetTag("payment.address", paymentAddress.Address);
            activity?.SetTag("payment.processing_time_ms", stopwatch.ElapsedMilliseconds);

            return MapToPaymentResponse(payment);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordPaymentFailed(request.CryptoCurrency.ToString(), ex.GetType().Name, stopwatch.Elapsed.TotalSeconds);
            
            _logger.LogError(ex, "Failed to create crypto payment for PaymentId: {PaymentId} after {ElapsedMs}ms",
                request.PaymentId, stopwatch.ElapsedMilliseconds);
                
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<PaymentResponse?> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetPaymentStatus");
        var stopwatch = Stopwatch.StartNew();
        
        activity?.SetTag("payment.id", paymentId);
        
        var logContext = new Dictionary<string, object>
        {
            ["PaymentId"] = paymentId,
            ["Operation"] = "GetPaymentStatus"
        };
        _logContextEnricher.EnrichContext(logContext);
        
        using var logScope = _logger.BeginScope(logContext);
        
        _logger.LogDebug("Retrieving payment status for PaymentId: {PaymentId}", paymentId);

        try
        {
            var payment = await _context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

            stopwatch.Stop();
            _metrics.RecordDatabaseOperation("get_payment_status", stopwatch.Elapsed.TotalSeconds);

            if (payment == null)
            {
                _logger.LogWarning("Payment not found for PaymentId: {PaymentId}", paymentId);
                activity?.SetTag("payment.found", false);
                return null;
            }

            _logger.LogDebug("Retrieved payment status: {Status} for PaymentId: {PaymentId} in {ElapsedMs}ms",
                payment.Status, paymentId, stopwatch.ElapsedMilliseconds);
                
            activity?.SetTag("payment.found", true);
            activity?.SetTag("payment.status", payment.Status.ToString());
            activity?.SetTag("payment.currency", payment.CryptoCurrency.Symbol);

            return MapToPaymentResponse(payment);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to retrieve payment status for PaymentId: {PaymentId} after {ElapsedMs}ms",
                paymentId, stopwatch.ElapsedMilliseconds);
                
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<PaymentResponse?> GetPaymentByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetPaymentById");
        var stopwatch = Stopwatch.StartNew();
        
        activity?.SetTag("payment.db_id", id);
        
        var logContext = new Dictionary<string, object>
        {
            ["PaymentDbId"] = id,
            ["Operation"] = "GetPaymentById"
        };
        _logContextEnricher.EnrichContext(logContext);
        
        using var logScope = _logger.BeginScope(logContext);

        try
        {
            var payment = await _context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            stopwatch.Stop();
            _metrics.RecordDatabaseOperation("get_payment_by_id", stopwatch.Elapsed.TotalSeconds);

            if (payment == null)
            {
                _logger.LogWarning("Payment not found for ID: {PaymentDbId}", id);
                activity?.SetTag("payment.found", false);
                return null;
            }

            _logger.LogDebug("Retrieved payment by ID: {PaymentDbId}, PaymentId: {PaymentId}, Status: {Status} in {ElapsedMs}ms",
                id, payment.PaymentId, payment.Status, stopwatch.ElapsedMilliseconds);
                
            activity?.SetTag("payment.found", true);
            activity?.SetTag("payment.id", payment.PaymentId);
            activity?.SetTag("payment.status", payment.Status.ToString());

            return MapToPaymentResponse(payment);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to retrieve payment by ID: {PaymentDbId} after {ElapsedMs}ms",
                id, stopwatch.ElapsedMilliseconds);
                
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<PaymentResponse> UpdatePaymentStatusAsync(int paymentId, PaymentStatus status, 
        string? transactionHash = null, decimal? receivedAmount = null, int? confirmations = null, 
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("UpdatePaymentStatus");
        var stopwatch = Stopwatch.StartNew();
        
        var logContext = new Dictionary<string, object>
        {
            ["PaymentDbId"] = paymentId,
            ["NewStatus"] = status.ToString(),
            ["TransactionHash"] = transactionHash ?? "null",
            ["ReceivedAmount"] = receivedAmount?.ToString() ?? "null",
            ["Confirmations"] = confirmations?.ToString() ?? "null"
        };
        _logContextEnricher.EnrichContext(logContext);
        
        activity?.SetTag("payment.db_id", paymentId);
        activity?.SetTag("payment.new_status", status.ToString());
        activity?.SetTag("payment.transaction_hash", transactionHash ?? "null");
        
        using var logScope = _logger.BeginScope(logContext);

        _logger.LogInformation("Updating payment status for PaymentDbId: {PaymentDbId} to {NewStatus}",
            paymentId, status);

        try
        {
            var payment = await _context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
                
            if (payment == null)
            {
                var errorMessage = $"Payment with ID {paymentId} not found";
                _logger.LogError("Payment not found for update: PaymentDbId: {PaymentDbId}", paymentId);
                activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            var oldStatus = payment.Status;
            var oldProcessingTime = payment.CreatedAt;
            
            payment.Status = status;
            
            if (!string.IsNullOrEmpty(transactionHash))
                payment.TransactionHash = transactionHash;
            
            if (receivedAmount.HasValue)
                payment.ReceivedAmount = receivedAmount.Value;
            
            if (confirmations.HasValue)
                payment.Confirmations = confirmations.Value;

            if (status == PaymentStatus.Confirmed || status == PaymentStatus.Paid)
                payment.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            
            stopwatch.Stop();
            _metrics.RecordDatabaseOperation("update_payment_status", stopwatch.Elapsed.TotalSeconds);

            // Record metrics based on status change
            if (oldStatus != status)
            {
                var processingTime = (DateTime.UtcNow - oldProcessingTime).TotalSeconds;
                
                switch (status)
                {
                    case PaymentStatus.Confirmed:
                    case PaymentStatus.Paid:
                        _metrics.RecordPaymentCompleted(payment.CryptoCurrency.Symbol, processingTime);
                        break;
                    case PaymentStatus.Failed:
                    case PaymentStatus.Expired:
                    case PaymentStatus.Cancelled:
                        _metrics.RecordPaymentFailed(payment.CryptoCurrency.Symbol, status.ToString(), processingTime);
                        break;
                }

                // Publish integration event
                var integrationEvent = new CryptoPaymentStatusChangedIntegrationEvent(
                    payment.Id, payment.PaymentId, oldStatus, status, payment.TransactionHash, payment.ReceivedAmount);
                
                await _integrationEventService.PublishEventsThroughEventBusAsync(integrationEvent);

                _logger.LogInformation("Payment status updated: PaymentDbId: {PaymentDbId}, PaymentId: {PaymentId} from {OldStatus} to {NewStatus} in {ElapsedMs}ms",
                    payment.Id, payment.PaymentId, oldStatus, status, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogDebug("Payment status unchanged: PaymentDbId: {PaymentDbId}, Status: {Status}",
                    payment.Id, status);
            }

            activity?.SetTag("payment.old_status", oldStatus.ToString());
            activity?.SetTag("payment.id", payment.PaymentId);
            activity?.SetTag("payment.processing_time_ms", stopwatch.ElapsedMilliseconds);

            return MapToPaymentResponse(payment);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to update payment status for PaymentDbId: {PaymentDbId} after {ElapsedMs}ms",
                paymentId, stopwatch.ElapsedMilliseconds);
                
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentResponse>> GetPaymentsByBuyerIdAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetPaymentsByBuyerId");
        var stopwatch = Stopwatch.StartNew();
        
        activity?.SetTag("buyer.id", buyerId);
        
        var logContext = new Dictionary<string, object>
        {
            ["BuyerId"] = buyerId,
            ["Operation"] = "GetPaymentsByBuyerId"
        };
        _logContextEnricher.EnrichContext(logContext);
        
        using var logScope = _logger.BeginScope(logContext);

        _logger.LogDebug("Retrieving payments for BuyerId: {BuyerId}", buyerId);

        try
        {
            var payments = await _context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .Where(p => p.BuyerId == buyerId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);

            stopwatch.Stop();
            _metrics.RecordDatabaseOperation("get_payments_by_buyer", stopwatch.Elapsed.TotalSeconds);

            _logger.LogDebug("Retrieved {PaymentCount} payments for BuyerId: {BuyerId} in {ElapsedMs}ms",
                payments.Count, buyerId, stopwatch.ElapsedMilliseconds);
                
            activity?.SetTag("payments.count", payments.Count);
            activity?.SetTag("query.processing_time_ms", stopwatch.ElapsedMilliseconds);

            return payments.Select(MapToPaymentResponse);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to retrieve payments for BuyerId: {BuyerId} after {ElapsedMs}ms",
                buyerId, stopwatch.ElapsedMilliseconds);
                
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task ExpirePaymentsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("ExpirePayments");
        var stopwatch = Stopwatch.StartNew();
        
        var logContext = new Dictionary<string, object>
        {
            ["Operation"] = "ExpirePayments"
        };
        _logContextEnricher.EnrichContext(logContext);
        
        using var logScope = _logger.BeginScope(logContext);

        _logger.LogDebug("Starting payment expiration process");

        try
        {
            var expiredPayments = await _context.CryptoPayments
                .Where(p => p.Status == PaymentStatus.Pending && 
                           p.ExpiresAt.HasValue && 
                           p.ExpiresAt < DateTime.UtcNow)
                .ToListAsync(cancellationToken);

            if (expiredPayments.Any())
            {
                foreach (var payment in expiredPayments)
                {
                    var processingTime = (DateTime.UtcNow - payment.CreatedAt).TotalSeconds;
                    payment.Status = PaymentStatus.Expired;
                    _metrics.RecordPaymentFailed("unknown", "expired", processingTime);
                }

                await _context.SaveChangesAsync(cancellationToken);
                
                stopwatch.Stop();
                _metrics.RecordDatabaseOperation("expire_payments", stopwatch.Elapsed.TotalSeconds);

                _logger.LogInformation("Expired {ExpiredCount} payments in {ElapsedMs}ms",
                    expiredPayments.Count, stopwatch.ElapsedMilliseconds);
                    
                activity?.SetTag("payments.expired_count", expiredPayments.Count);
            }
            else
            {
                stopwatch.Stop();
                _logger.LogDebug("No payments to expire. Process completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            }

            activity?.SetTag("processing_time_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to expire payments after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static PaymentResponse MapToPaymentResponse(CryptoPayment payment)
    {
        return new PaymentResponse
        {
            Id = payment.Id,
            PaymentId = payment.PaymentId,
            CryptoCurrency = payment.CryptoCurrency.Symbol,
            PaymentAddress = payment.PaymentAddress.Address,
            RequestedAmount = payment.RequestedAmount,
            ReceivedAmount = payment.ReceivedAmount,
            Status = payment.Status,
            TransactionHash = payment.TransactionHash,
            Confirmations = payment.Confirmations,
            RequiredConfirmations = payment.RequiredConfirmations,
            CreatedAt = payment.CreatedAt,
            CompletedAt = payment.CompletedAt,
            ExpiresAt = payment.ExpiresAt,
            ErrorMessage = payment.ErrorMessage
        };
    }
}