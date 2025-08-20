using eShop.Ordering.Domain.Specifications;

namespace eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

//This is just the RepositoryContracts or Interface defined at the Domain Layer
//as requisite for the Order Aggregate

public interface IOrderRepository : IRepository<Order>
{
    Order Add(Order order);

    void Update(Order order);

    Task<Order> GetAsync(int orderId);
    
    // Crypto payment specific methods
    Task<Order?> GetByCryptoPaymentIdAsync(string cryptoPaymentId);
    Task<Order?> GetByCryptoTransactionHashAsync(string transactionHash);
    Task<IEnumerable<Order>> GetOrdersWithPendingCryptoPaymentsAsync();
    Task<IEnumerable<Order>> GetOrdersWithPendingCryptoPaymentsAsync(DateTime olderThan);
    Task<IEnumerable<Order>> GetOrdersWithConfirmedCryptoPaymentsAsync(DateTime fromDate, DateTime toDate);
    Task<IEnumerable<Order>> GetOrdersWithCryptoPaymentsByBuyerAsync(int buyerId);
    Task<IEnumerable<Order>> GetOrdersWithCryptoPaymentsByBuyerAsync(string buyerIdentityGuid);
    
    // Specification pattern methods for crypto payments
    Task<IEnumerable<Order>> ListAsync(ISpecification<Order> specification);
    Task<int> CountAsync(ISpecification<Order> specification);
}
