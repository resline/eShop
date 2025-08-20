namespace eShop.CryptoPayment.API.Infrastructure.EntityConfigurations;

public class CryptoCurrencyEntityTypeConfiguration : IEntityTypeConfiguration<CryptoCurrency>
{
    public void Configure(EntityTypeBuilder<CryptoCurrency> builder)
    {
        builder.ToTable("CryptoCurrencies");

        builder.HasKey(cc => cc.Id);

        builder.Property(cc => cc.Symbol)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(cc => cc.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cc => cc.NetworkName)
            .HasMaxLength(50);

        builder.Property(cc => cc.ContractAddress)
            .HasMaxLength(100);

        builder.Property(cc => cc.CreatedAt)
            .IsRequired();

        builder.Property(cc => cc.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(cc => cc.Symbol)
            .IsUnique();

        // Seed data
        builder.HasData(
            new CryptoCurrency
            {
                Id = 1,
                Symbol = "BTC",
                Name = "Bitcoin",
                Decimals = 8,
                NetworkName = "Bitcoin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new CryptoCurrency
            {
                Id = 2,
                Symbol = "ETH",
                Name = "Ethereum",
                Decimals = 18,
                NetworkName = "Ethereum",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );
    }
}