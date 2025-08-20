namespace eShop.Ordering.Infrastructure.EntityConfigurations;

class BuyerEntityTypeConfiguration
    : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> buyerConfiguration)
    {
        buyerConfiguration.ToTable("buyers");

        buyerConfiguration.Ignore(b => b.DomainEvents);

        buyerConfiguration.Property(b => b.Id)
            .UseHiLo("buyerseq");

        buyerConfiguration.Property(b => b.IdentityGuid)
            .HasMaxLength(200);

        buyerConfiguration.HasIndex("IdentityGuid")
            .IsUnique(true);

        buyerConfiguration.HasMany(b => b.PaymentMethods)
            .WithOne();

        // Configure crypto wallets as owned entities
        buyerConfiguration.OwnsMany(b => b.CryptoWallets, cw =>
        {
            cw.ToTable("buyer_crypto_wallets");
            cw.WithOwner().HasForeignKey("BuyerId");
            cw.Property<int>("Id");
            cw.HasKey("Id");
            
            cw.Property(w => w.Address)
                .IsRequired()
                .HasMaxLength(100);
                
            cw.Property(w => w.Label)
                .HasMaxLength(100);
                
            // Configure BlockchainNetwork as owned value object
            cw.OwnsOne(w => w.Network, n =>
            {
                n.Property(net => net.Id).HasColumnName("NetworkId");
                n.Property(net => net.Name).HasColumnName("NetworkName").HasMaxLength(50);
                n.Property(net => net.ChainId).HasColumnName("ChainId").HasMaxLength(20);
                n.Property(net => net.NativeCurrency).HasColumnName("NativeCurrency").HasMaxLength(10);
                n.Property(net => net.IsTestnet).HasColumnName("IsTestnet");
            });
            
            cw.HasIndex(w => w.Address);
        });
    }
}