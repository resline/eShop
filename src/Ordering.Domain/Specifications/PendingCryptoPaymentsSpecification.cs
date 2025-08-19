using System.Linq.Expressions;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.Domain.Specifications;

public class PendingCryptoPaymentsSpecification : BaseSpecification<Order>
{
    public PendingCryptoPaymentsSpecification() 
        : base(order => !string.IsNullOrEmpty(order.CryptoPaymentId) 
                       && order.CryptoPaymentInitiatedAt.HasValue 
                       && !order.CryptoPaymentConfirmedAt.HasValue)
    {
        ApplyOrderBy(order => order.CryptoPaymentInitiatedAt);
    }

    public PendingCryptoPaymentsSpecification(DateTime olderThan) 
        : base(order => !string.IsNullOrEmpty(order.CryptoPaymentId) 
                       && order.CryptoPaymentInitiatedAt.HasValue 
                       && !order.CryptoPaymentConfirmedAt.HasValue
                       && order.CryptoPaymentInitiatedAt < olderThan)
    {
        ApplyOrderBy(order => order.CryptoPaymentInitiatedAt);
    }

    public PendingCryptoPaymentsSpecification(int skip, int take) 
        : base(order => !string.IsNullOrEmpty(order.CryptoPaymentId) 
                       && order.CryptoPaymentInitiatedAt.HasValue 
                       && !order.CryptoPaymentConfirmedAt.HasValue)
    {
        ApplyOrderBy(order => order.CryptoPaymentInitiatedAt);
        ApplyPaging(skip, take);
    }
}