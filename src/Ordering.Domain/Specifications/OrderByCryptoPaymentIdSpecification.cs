using System.Linq.Expressions;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.Domain.Specifications;

public class OrderByCryptoPaymentIdSpecification : BaseSpecification<Order>
{
    public OrderByCryptoPaymentIdSpecification(string cryptoPaymentId) 
        : base(order => order.CryptoPaymentId == cryptoPaymentId)
    {
        AddInclude(order => order.OrderItems);
        AddInclude(order => order.Buyer);
    }
}