namespace eShop.CryptoPayment.API.Infrastructure.EntityConfigurations;

public class CryptoPaymentEntityTypeConfiguration : IEntityTypeConfiguration<CryptoPayment>
{
    public void Configure(EntityTypeBuilder<CryptoPayment> builder)
    {
        builder.ToTable("CryptoPayments");

        builder.HasKey(cp => cp.Id);

        builder.Property(cp => cp.PaymentId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cp => cp.RequestedAmount)
            .HasPrecision(18, 8)
            .IsRequired();

        builder.Property(cp => cp.ReceivedAmount)
            .HasPrecision(18, 8);

        builder.Property(cp => cp.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(cp => cp.TransactionHash)
            .HasMaxLength(100);

        builder.Property(cp => cp.ErrorMessage)
            .HasMaxLength(500);

        builder.Property(cp => cp.BuyerId)
            .HasMaxLength(100);

        builder.Property(cp => cp.Metadata)
            .HasColumnType("jsonb");

        builder.Property(cp => cp.CreatedAt)
            .IsRequired();

        // Relationships
        builder.HasOne(cp => cp.CryptoCurrency)
            .WithMany(cc => cc.Payments)
            .HasForeignKey(cp => cp.CryptoCurrencyId);

        builder.HasOne(cp => cp.PaymentAddress)
            .WithMany(pa => pa.Payments)
            .HasForeignKey(cp => cp.PaymentAddressId);

        // Indexes
        builder.HasIndex(cp => cp.PaymentId);
        builder.HasIndex(cp => cp.TransactionHash);
        builder.HasIndex(cp => cp.Status);
        builder.HasIndex(cp => cp.BuyerId);
        builder.HasIndex(cp => cp.CreatedAt);
    }
}