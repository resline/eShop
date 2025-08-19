namespace eShop.CryptoPayment.API.Services;

public interface ICryptoPaymentService
{
    Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse?> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentResponse?> GetPaymentByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PaymentResponse> UpdatePaymentStatusAsync(int paymentId, PaymentStatus status, string? transactionHash = null, 
        decimal? receivedAmount = null, int? confirmations = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentResponse>> GetPaymentsByBuyerIdAsync(string buyerId, CancellationToken cancellationToken = default);
    Task ExpirePaymentsAsync(CancellationToken cancellationToken = default);
}