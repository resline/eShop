using System.Linq.Expressions;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.Domain.Specifications;

public class ConfirmedCryptoPaymentsSpecification : BaseSpecification<Order>
{
    public ConfirmedCryptoPaymentsSpecification() 
        : base(order => !string.IsNullOrEmpty(order.CryptoPaymentId) 
                       && order.CryptoPaymentConfirmedAt.HasValue)
    {
        ApplyOrderByDescending(order => order.CryptoPaymentConfirmedAt);
    }

    public ConfirmedCryptoPaymentsSpecification(DateTime fromDate, DateTime toDate) 
        : base(order => !string.IsNullOrEmpty(order.CryptoPaymentId) 
                       && order.CryptoPaymentConfirmedAt.HasValue
                       && order.CryptoPaymentConfirmedAt >= fromDate
                       && order.CryptoPaymentConfirmedAt <= toDate)
    {
        ApplyOrderByDescending(order => order.CryptoPaymentConfirmedAt);
    }

    public ConfirmedCryptoPaymentsSpecification(string transactionHash) 
        : base(order => order.CryptoTransactionHash == transactionHash 
                       && order.CryptoPaymentConfirmedAt.HasValue)
    {
        ApplyOrderByDescending(order => order.CryptoPaymentConfirmedAt);
    }

    public ConfirmedCryptoPaymentsSpecification(int buyerId, DateTime fromDate, DateTime toDate) 
        : base(order => order.BuyerId == buyerId
                       && !string.IsNullOrEmpty(order.CryptoPaymentId) 
                       && order.CryptoPaymentConfirmedAt.HasValue
                       && order.CryptoPaymentConfirmedAt >= fromDate
                       && order.CryptoPaymentConfirmedAt <= toDate)
    {
        ApplyOrderByDescending(order => order.CryptoPaymentConfirmedAt);
    }
}