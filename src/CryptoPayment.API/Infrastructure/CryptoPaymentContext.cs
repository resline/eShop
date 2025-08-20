using eShop.IntegrationEventLogEF;

namespace eShop.CryptoPayment.API.Infrastructure;

public class CryptoPaymentContext : DbContext, IIntegrationEventLogContext
{
    public CryptoPaymentContext(DbContextOptions<CryptoPaymentContext> options) : base(options)
    {
    }

    public DbSet<CryptoPayment> CryptoPayments { get; set; }
    public DbSet<CryptoCurrency> CryptoCurrencies { get; set; }
    public DbSet<PaymentAddress> PaymentAddresses { get; set; }
    public DbSet<IntegrationEventLogEntry> IntegrationEventLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CryptoPaymentEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new CryptoCurrencyEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentAddressEntityTypeConfiguration());
        modelBuilder.UseIntegrationEventLogs();
    }

    public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
    {
        var result = await SaveChangesAsync(cancellationToken);
        return result > 0;
    }
}