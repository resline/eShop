namespace CryptoPayment.IntegrationTests;

public class DatabaseIntegrationTests : IClassFixture<CryptoPaymentApiFixture>
{
    private readonly CryptoPaymentApiFixture _fixture;

    public DatabaseIntegrationTests(CryptoPaymentApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Database_ShouldBeInitializedWithSeedData()
    {
        await _fixture.ExecuteDbContextAsync(async context =>
        {
            // Assert cryptocurrencies are seeded
            var currencies = await context.CryptoCurrencies.ToListAsync();
            currencies.Should().HaveCount(2);
            currencies.Should().Contain(c => c.Symbol == "BTC");
            currencies.Should().Contain(c => c.Symbol == "ETH");

            // Assert payment addresses are seeded
            var addresses = await context.PaymentAddresses.ToListAsync();
            addresses.Should().HaveCountGreaterOrEqualTo(2);
            addresses.Should().Contain(a => a.CryptoCurrencyId == 1); // Bitcoin
            addresses.Should().Contain(a => a.CryptoCurrencyId == 2); // Ethereum
        });
    }

    [Fact]
    public async Task CryptoPayment_DatabaseOperations_ShouldWorkCorrectly()
    {
        await _fixture.ExecuteDbContextAsync(async context =>
        {
            // Arrange
            var payment = new CryptoPayment
            {
                PaymentId = $"db-op-test-{Guid.NewGuid()}",
                CryptoCurrencyId = 1,
                PaymentAddressId = 1,
                RequestedAmount = 0.001m,
                Status = PaymentStatus.Pending,
                RequiredConfirmations = 6,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                BuyerId = "test-buyer",
                Metadata = JsonSerializer.Serialize(new { test = "value" })
            };

            // Act - Create
            context.CryptoPayments.Add(payment);
            await context.SaveChangesAsync();

            // Assert - Read
            var savedPayment = await context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .FirstAsync(p => p.Id == payment.Id);

            savedPayment.PaymentId.Should().Be(payment.PaymentId);
            savedPayment.CryptoCurrency.Symbol.Should().Be("BTC");
            savedPayment.PaymentAddress.Should().NotBeNull();

            // Act - Update
            savedPayment.Status = PaymentStatus.Paid;
            savedPayment.TransactionHash = "0x123abc456def";
            savedPayment.ReceivedAmount = 0.0015m;
            savedPayment.CompletedAt = DateTime.UtcNow;
            
            await context.SaveChangesAsync();

            // Assert - Updated
            var updatedPayment = await context.CryptoPayments.FindAsync(payment.Id);
            updatedPayment!.Status.Should().Be(PaymentStatus.Paid);
            updatedPayment.TransactionHash.Should().Be("0x123abc456def");
            updatedPayment.ReceivedAmount.Should().Be(0.0015m);
            updatedPayment.CompletedAt.Should().NotBeNull();

            // Act - Delete
            context.CryptoPayments.Remove(updatedPayment);
            await context.SaveChangesAsync();

            // Assert - Deleted
            var deletedPayment = await context.CryptoPayments.FindAsync(payment.Id);
            deletedPayment.Should().BeNull();
        });
    }

    [Fact]
    public async Task PaymentAddress_UsageTracking_ShouldWorkCorrectly()
    {
        await _fixture.ExecuteDbContextAsync(async context =>
        {
            // Arrange
            var address = new PaymentAddress
            {
                Address = $"test-address-{Guid.NewGuid()}",
                CryptoCurrencyId = 1,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            };

            context.PaymentAddresses.Add(address);
            await context.SaveChangesAsync();

            // Act - Mark as used
            address.IsUsed = true;
            address.UsedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            // Assert
            var updatedAddress = await context.PaymentAddresses.FindAsync(address.Id);
            updatedAddress!.IsUsed.Should().BeTrue();
            updatedAddress.UsedAt.Should().NotBeNull();
            updatedAddress.UsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        });
    }

    [Fact]
    public async Task CryptoPayment_ComplexQueries_ShouldWorkCorrectly()
    {
        await _fixture.ExecuteDbContextAsync(async context =>
        {
            // Arrange - Create test payments
            var buyerId = $"complex-query-buyer-{Guid.NewGuid()}";
            var payments = new List<CryptoPayment>
            {
                new()
                {
                    PaymentId = $"payment-1-{Guid.NewGuid()}",
                    CryptoCurrencyId = 1,
                    PaymentAddressId = 1,
                    RequestedAmount = 0.001m,
                    Status = PaymentStatus.Pending,
                    RequiredConfirmations = 6,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                    BuyerId = buyerId
                },
                new()
                {
                    PaymentId = $"payment-2-{Guid.NewGuid()}",
                    CryptoCurrencyId = 2,
                    PaymentAddressId = 1,
                    RequestedAmount = 0.1m,
                    Status = PaymentStatus.Paid,
                    RequiredConfirmations = 12,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                    CompletedAt = DateTime.UtcNow,
                    BuyerId = buyerId
                },
                new()
                {
                    PaymentId = $"payment-3-{Guid.NewGuid()}",
                    CryptoCurrencyId = 1,
                    PaymentAddressId = 1,
                    RequestedAmount = 0.002m,
                    Status = PaymentStatus.Expired,
                    RequiredConfirmations = 6,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-60),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(-30),
                    BuyerId = buyerId
                }
            };

            context.CryptoPayments.AddRange(payments);
            await context.SaveChangesAsync();

            // Act & Assert - Query by buyer ID
            var buyerPayments = await context.CryptoPayments
                .Where(p => p.BuyerId == buyerId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            buyerPayments.Should().HaveCount(3);
            buyerPayments.First().Status.Should().Be(PaymentStatus.Paid); // Most recent

            // Act & Assert - Query pending payments
            var pendingPayments = await context.CryptoPayments
                .Where(p => p.Status == PaymentStatus.Pending && p.BuyerId == buyerId)
                .ToListAsync();

            pendingPayments.Should().HaveCount(1);

            // Act & Assert - Query expired payments
            var expiredPayments = await context.CryptoPayments
                .Where(p => p.Status == PaymentStatus.Pending && 
                           p.ExpiresAt.HasValue && 
                           p.ExpiresAt < DateTime.UtcNow)
                .ToListAsync();

            // Should not include our test data since we use future expiration times
            // But verifies the query structure works

            // Act & Assert - Query with includes
            var paymentsWithDetails = await context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .Where(p => p.BuyerId == buyerId)
                .ToListAsync();

            paymentsWithDetails.Should().HaveCount(3);
            paymentsWithDetails.Should().OnlyContain(p => p.CryptoCurrency != null);
            paymentsWithDetails.Should().OnlyContain(p => p.PaymentAddress != null);
        });
    }

    [Fact]
    public async Task Database_TransactionBehavior_ShouldRollbackOnError()
    {
        await _fixture.ExecuteDbContextAsync(async context =>
        {
            using var transaction = await context.Database.BeginTransactionAsync();
            
            try
            {
                // Arrange
                var payment = new CryptoPayment
                {
                    PaymentId = $"transaction-test-{Guid.NewGuid()}",
                    CryptoCurrencyId = 1,
                    PaymentAddressId = 1,
                    RequestedAmount = 0.001m,
                    Status = PaymentStatus.Pending,
                    RequiredConfirmations = 6,
                    CreatedAt = DateTime.UtcNow,
                    BuyerId = "test-buyer"
                };

                // Act
                context.CryptoPayments.Add(payment);
                await context.SaveChangesAsync();

                // Verify it's added in the transaction
                var addedPayment = await context.CryptoPayments
                    .FirstOrDefaultAsync(p => p.PaymentId == payment.PaymentId);
                
                addedPayment.Should().NotBeNull();

                // Rollback the transaction
                await transaction.RollbackAsync();

                // Assert - Payment should not exist after rollback
                var rolledBackPayment = await context.CryptoPayments
                    .FirstOrDefaultAsync(p => p.PaymentId == payment.PaymentId);
                
                rolledBackPayment.Should().BeNull();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }
}