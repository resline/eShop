using System.Text.Json;

namespace eShop.CryptoPayment.API.Services;

public class CryptoPaymentService : ICryptoPaymentService
{
    private readonly CryptoPaymentContext _context;
    private readonly IAddressGenerationService _addressService;
    private readonly ICryptoPaymentIntegrationEventService _integrationEventService;
    private readonly ILogger<CryptoPaymentService> _logger;

    public CryptoPaymentService(
        CryptoPaymentContext context,
        IAddressGenerationService addressService,
        ICryptoPaymentIntegrationEventService integrationEventService,
        ILogger<CryptoPaymentService> logger)
    {
        _context = context;
        _addressService = addressService;
        _integrationEventService = integrationEventService;
        _logger = logger;
    }

    public async Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating crypto payment for PaymentId: {PaymentId}", request.PaymentId);

        // Check if payment already exists
        var existingPayment = await _context.CryptoPayments
            .Include(p => p.CryptoCurrency)
            .Include(p => p.PaymentAddress)
            .FirstOrDefaultAsync(p => p.PaymentId == request.PaymentId, cancellationToken);

        if (existingPayment != null)
        {
            _logger.LogWarning("Payment already exists for PaymentId: {PaymentId}", request.PaymentId);
            return MapToPaymentResponse(existingPayment);
        }

        // Get or generate payment address
        var paymentAddress = await _addressService.GetUnusedAddressAsync(request.CryptoCurrency, cancellationToken)
            ?? await _addressService.GenerateAddressAsync(request.CryptoCurrency, cancellationToken);

        // Get cryptocurrency info
        var cryptoCurrency = await _context.CryptoCurrencies
            .FirstOrDefaultAsync(c => c.Id == (int)request.CryptoCurrency, cancellationToken)
            ?? throw new InvalidOperationException($"Cryptocurrency {request.CryptoCurrency} not found");

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

        _logger.LogInformation("Created crypto payment with ID: {PaymentId}", payment.Id);

        return MapToPaymentResponse(payment);
    }

    public async Task<PaymentResponse?> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.CryptoPayments
            .Include(p => p.CryptoCurrency)
            .Include(p => p.PaymentAddress)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        return payment != null ? MapToPaymentResponse(payment) : null;
    }

    public async Task<PaymentResponse?> GetPaymentByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var payment = await _context.CryptoPayments
            .Include(p => p.CryptoCurrency)
            .Include(p => p.PaymentAddress)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return payment != null ? MapToPaymentResponse(payment) : null;
    }

    public async Task<PaymentResponse> UpdatePaymentStatusAsync(int paymentId, PaymentStatus status, 
        string? transactionHash = null, decimal? receivedAmount = null, int? confirmations = null, 
        CancellationToken cancellationToken = default)
    {
        var payment = await _context.CryptoPayments
            .Include(p => p.CryptoCurrency)
            .Include(p => p.PaymentAddress)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment with ID {paymentId} not found");

        var oldStatus = payment.Status;
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

        // Publish integration event if status changed
        if (oldStatus != status)
        {
            var integrationEvent = new CryptoPaymentStatusChangedIntegrationEvent(
                payment.Id, payment.PaymentId, oldStatus, status, payment.TransactionHash, payment.ReceivedAmount);
            
            await _integrationEventService.PublishEventsThroughEventBusAsync(integrationEvent);
        }

        _logger.LogInformation("Updated payment {PaymentId} status from {OldStatus} to {NewStatus}", 
            payment.Id, oldStatus, status);

        return MapToPaymentResponse(payment);
    }

    public async Task<IEnumerable<PaymentResponse>> GetPaymentsByBuyerIdAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var payments = await _context.CryptoPayments
            .Include(p => p.CryptoCurrency)
            .Include(p => p.PaymentAddress)
            .Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return payments.Select(MapToPaymentResponse);
    }

    public async Task<PaymentResponse?> GetPaymentByTransactionHashAsync(string transactionHash, CancellationToken cancellationToken = default)
    {
        var payment = await _context.CryptoPayments
            .Include(p => p.CryptoCurrency)
            .Include(p => p.PaymentAddress)
            .FirstOrDefaultAsync(p => p.TransactionHash == transactionHash, cancellationToken);

        return payment != null ? MapToPaymentResponse(payment) : null;
    }

    public async Task<bool> CanUserAccessPaymentAsync(string paymentId, string userId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.CryptoPayments
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        return payment?.BuyerId == userId;
    }

    public async Task ExpirePaymentsAsync(CancellationToken cancellationToken = default)
    {
        var expiredPayments = await _context.CryptoPayments
            .Where(p => p.Status == PaymentStatus.Pending && 
                       p.ExpiresAt.HasValue && 
                       p.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var payment in expiredPayments)
        {
            payment.Status = PaymentStatus.Expired;
        }

        if (expiredPayments.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Expired {Count} payments", expiredPayments.Count);
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