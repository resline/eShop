namespace eShop.CryptoPayment.API.Infrastructure.EntityConfigurations;

public class PaymentAddressEntityTypeConfiguration : IEntityTypeConfiguration<PaymentAddress>
{
    public void Configure(EntityTypeBuilder<PaymentAddress> builder)
    {
        builder.ToTable("PaymentAddresses");

        builder.HasKey(pa => pa.Id);

        builder.Property(pa => pa.Address)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pa => pa.PrivateKey)
            .HasMaxLength(200); // Encrypted storage

        builder.Property(pa => pa.CreatedAt)
            .IsRequired();

        // Relationships
        builder.HasOne(pa => pa.CryptoCurrency)
            .WithMany(cc => cc.PaymentAddresses)
            .HasForeignKey(pa => pa.CryptoCurrencyId);

        // Indexes
        builder.HasIndex(pa => pa.Address)
            .IsUnique();
        
        builder.HasIndex(pa => new { pa.CryptoCurrencyId, pa.IsUsed });
        builder.HasIndex(pa => pa.CreatedAt);
    }
}