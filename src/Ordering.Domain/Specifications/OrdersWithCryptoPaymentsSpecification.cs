using System.Linq.Expressions;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.Domain.Specifications;

public class OrdersWithCryptoPaymentsSpecification : BaseSpecification<Order>
{
    public OrdersWithCryptoPaymentsSpecification() 
        : base(order => !string.IsNullOrEmpty(order.CryptoPaymentId))
    {
        ApplyOrderByDescending(order => order.OrderDate);
    }

    public OrdersWithCryptoPaymentsSpecification(int buyerId) 
        : base(order => order.BuyerId == buyerId && !string.IsNullOrEmpty(order.CryptoPaymentId))
    {
        ApplyOrderByDescending(order => order.OrderDate);
    }

    public OrdersWithCryptoPaymentsSpecification(string buyerIdentityGuid) 
        : base(order => order.Buyer.IdentityGuid == buyerIdentityGuid && !string.IsNullOrEmpty(order.CryptoPaymentId))
    {
        AddInclude(order => order.Buyer);
        ApplyOrderByDescending(order => order.OrderDate);
    }
}