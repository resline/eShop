using System.Text.Json;

namespace CryptoPayment.UnitTests.Services;

public class CryptoPaymentServiceTests : IDisposable
{
    private readonly CryptoPaymentContext _context;
    private readonly Mock<IAddressGenerationService> _mockAddressService;
    private readonly Mock<ICryptoPaymentIntegrationEventService> _mockIntegrationEventService;
    private readonly Mock<ILogger<CryptoPaymentService>> _mockLogger;
    private readonly CryptoPaymentService _service;
    private readonly Fixture _fixture;

    public CryptoPaymentServiceTests()
    {
        _fixture = new Fixture();
        _fixture.Behaviors.OfType<ThrowingRecursionBehavior>().ToList()
            .ForEach(b => _fixture.Behaviors.Remove(b));
        _fixture.Behaviors.Add(new OmitOnRecursionBehavior());

        var options = new DbContextOptionsBuilder<CryptoPaymentContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new CryptoPaymentContext(options);
        SeedDatabase();

        _mockAddressService = new Mock<IAddressGenerationService>();
        _mockIntegrationEventService = new Mock<ICryptoPaymentIntegrationEventService>();
        _mockLogger = new Mock<ILogger<CryptoPaymentService>>();

        _service = new CryptoPaymentService(
            _context,
            _mockAddressService.Object,
            _mockIntegrationEventService.Object,
            _mockLogger.Object);
    }

    private void SeedDatabase()
    {
        _context.CryptoCurrencies.AddRange(
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

        _context.PaymentAddresses.Add(new PaymentAddress
        {
            Id = 1,
            Address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
            CryptoCurrencyId = 1,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        });

        _context.SaveChanges();
    }

    [Fact]
    public async Task CreatePaymentAsync_WithValidRequest_ShouldCreatePaymentSuccessfully()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "buyer-123",
            ExpirationMinutes = 30,
            Metadata = new Dictionary<string, object> { ["orderId"] = "order-456" }
        };

        var paymentAddress = new PaymentAddress
        {
            Id = 2,
            Address = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4",
            CryptoCurrencyId = 1,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _mockAddressService.Setup(x => x.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentAddress);

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.PaymentId.Should().Be(request.PaymentId);
        result.CryptoCurrency.Should().Be("BTC");
        result.RequestedAmount.Should().Be(request.Amount);
        result.Status.Should().Be(PaymentStatus.Pending);
        result.PaymentAddress.Should().Be(paymentAddress.Address);
        result.RequiredConfirmations.Should().Be(6); // Bitcoin default

        _mockIntegrationEventService.Verify(
            x => x.PublishEventsThroughEventBusAsync(It.IsAny<CryptoPaymentCreatedIntegrationEvent>()),
            Times.Once);

        _mockAddressService.Verify(
            x => x.MarkAddressAsUsedAsync(paymentAddress.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreatePaymentAsync_WithEthereum_ShouldSetCorrectConfirmations()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-eth-123",
            CryptoCurrency = CryptoCurrencyType.Ethereum,
            Amount = 0.1m,
            BuyerId = "buyer-123"
        };

        var paymentAddress = new PaymentAddress
        {
            Id = 3,
            Address = "0x742d35Cc6635C0532925a3b8D7386DdA9f2777c4",
            CryptoCurrencyId = 2,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _mockAddressService.Setup(x => x.GetUnusedAddressAsync(CryptoCurrencyType.Ethereum, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentAddress);

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.RequiredConfirmations.Should().Be(12); // Ethereum default
        result.CryptoCurrency.Should().Be("ETH");
    }

    [Fact]
    public async Task CreatePaymentAsync_WhenPaymentAlreadyExists_ShouldReturnExistingPayment()
    {
        // Arrange
        var existingPayment = await CreateTestPayment("existing-payment-123");
        
        var request = new CreatePaymentRequest
        {
            PaymentId = "existing-payment-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.002m,
            BuyerId = "buyer-456"
        };

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.PaymentId.Should().Be(existingPayment.PaymentId);
        result.RequestedAmount.Should().Be(existingPayment.RequestedAmount);
        
        // Should not create new payment or generate new address
        _mockAddressService.Verify(
            x => x.GetUnusedAddressAsync(It.IsAny<CryptoCurrencyType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreatePaymentAsync_WhenCryptoCurrencyNotFound_ShouldThrowException()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-unknown-123",
            CryptoCurrency = (CryptoCurrencyType)999, // Non-existent currency
            Amount = 0.001m,
            BuyerId = "buyer-123"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreatePaymentAsync(request));
        
        exception.Message.Should().Contain("Cryptocurrency");
        exception.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task CreatePaymentAsync_WhenNoAddressAvailable_ShouldGenerateNewAddress()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-no-address-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "buyer-123"
        };

        var generatedAddress = new PaymentAddress
        {
            Id = 4,
            Address = "bc1qnewaddress123",
            CryptoCurrencyId = 1,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _mockAddressService.Setup(x => x.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentAddress?)null);

        _mockAddressService.Setup(x => x.GenerateAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedAddress);

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.PaymentAddress.Should().Be(generatedAddress.Address);
        
        _mockAddressService.Verify(
            x => x.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()),
            Times.Once);
        
        _mockAddressService.Verify(
            x => x.GenerateAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_WithValidPaymentId_ShouldReturnPayment()
    {
        // Arrange
        var payment = await CreateTestPayment("status-test-123");

        // Act
        var result = await _service.GetPaymentStatusAsync(payment.PaymentId);

        // Assert
        result.Should().NotBeNull();
        result!.PaymentId.Should().Be(payment.PaymentId);
        result.Status.Should().Be(payment.Status);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_WithInvalidPaymentId_ShouldReturnNull()
    {
        // Act
        var result = await _service.GetPaymentStatusAsync("non-existent-payment");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPaymentByIdAsync_WithValidId_ShouldReturnPayment()
    {
        // Arrange
        var payment = await CreateTestPayment("id-test-123");

        // Act
        var result = await _service.GetPaymentByIdAsync(payment.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(payment.Id);
        result.PaymentId.Should().Be(payment.PaymentId);
    }

    [Fact]
    public async Task GetPaymentByIdAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _service.GetPaymentByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePaymentStatusAsync_WithValidData_ShouldUpdateSuccessfully()
    {
        // Arrange
        var payment = await CreateTestPayment("update-test-123");
        var transactionHash = "0x123abc456def";
        var receivedAmount = 0.0015m;
        var confirmations = 3;

        // Act
        var result = await _service.UpdatePaymentStatusAsync(
            payment.Id, 
            PaymentStatus.Paid, 
            transactionHash, 
            receivedAmount, 
            confirmations);

        // Assert
        result.Status.Should().Be(PaymentStatus.Paid);
        result.TransactionHash.Should().Be(transactionHash);
        result.ReceivedAmount.Should().Be(receivedAmount);
        result.Confirmations.Should().Be(confirmations);
        result.CompletedAt.Should().NotBeNull();

        _mockIntegrationEventService.Verify(
            x => x.PublishEventsThroughEventBusAsync(It.IsAny<CryptoPaymentStatusChangedIntegrationEvent>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePaymentStatusAsync_WithConfirmedStatus_ShouldSetCompletedAt()
    {
        // Arrange
        var payment = await CreateTestPayment("confirm-test-123");

        // Act
        var result = await _service.UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Confirmed);

        // Assert
        result.Status.Should().Be(PaymentStatus.Confirmed);
        result.CompletedAt.Should().NotBeNull();
        result.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdatePaymentStatusAsync_WithSameStatus_ShouldNotPublishEvent()
    {
        // Arrange
        var payment = await CreateTestPayment("same-status-test-123");

        // Act
        var result = await _service.UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Pending);

        // Assert
        result.Status.Should().Be(PaymentStatus.Pending);
        
        _mockIntegrationEventService.Verify(
            x => x.PublishEventsThroughEventBusAsync(It.IsAny<CryptoPaymentStatusChangedIntegrationEvent>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdatePaymentStatusAsync_WithInvalidPaymentId_ShouldThrowException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdatePaymentStatusAsync(999, PaymentStatus.Paid));
        
        exception.Message.Should().Contain("Payment with ID 999 not found");
    }

    [Fact]
    public async Task GetPaymentsByBuyerIdAsync_WithValidBuyerId_ShouldReturnPayments()
    {
        // Arrange
        var buyerId = "buyer-multi-test-123";
        await CreateTestPayment("payment-1", buyerId);
        await CreateTestPayment("payment-2", buyerId);
        await CreateTestPayment("payment-3", "different-buyer");

        // Act
        var result = await _service.GetPaymentsByBuyerIdAsync(buyerId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.PaymentId.Contains("payment-"));
        result.Should().BeInDescendingOrder(p => p.CreatedAt);
    }

    [Fact]
    public async Task GetPaymentsByBuyerIdAsync_WithNonExistentBuyerId_ShouldReturnEmpty()
    {
        // Act
        var result = await _service.GetPaymentsByBuyerIdAsync("non-existent-buyer");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpirePaymentsAsync_ShouldExpireOnlyPendingPaymentsPassedExpirationTime()
    {
        // Arrange
        var expiredPayment1 = await CreateTestPayment("expired-1", "buyer-1", DateTime.UtcNow.AddMinutes(-10));
        var expiredPayment2 = await CreateTestPayment("expired-2", "buyer-2", DateTime.UtcNow.AddMinutes(-5));
        var validPayment = await CreateTestPayment("valid-1", "buyer-3", DateTime.UtcNow.AddMinutes(10));
        
        var confirmedExpiredPayment = await CreateTestPayment("confirmed-expired", "buyer-4", DateTime.UtcNow.AddMinutes(-10));
        await _service.UpdatePaymentStatusAsync(confirmedExpiredPayment.Id, PaymentStatus.Confirmed);

        // Act
        await _service.ExpirePaymentsAsync();

        // Assert
        var expiredResult1 = await _service.GetPaymentByIdAsync(expiredPayment1.Id);
        var expiredResult2 = await _service.GetPaymentByIdAsync(expiredPayment2.Id);
        var validResult = await _service.GetPaymentByIdAsync(validPayment.Id);
        var confirmedResult = await _service.GetPaymentByIdAsync(confirmedExpiredPayment.Id);

        expiredResult1!.Status.Should().Be(PaymentStatus.Expired);
        expiredResult2!.Status.Should().Be(PaymentStatus.Expired);
        validResult!.Status.Should().Be(PaymentStatus.Pending);
        confirmedResult!.Status.Should().Be(PaymentStatus.Confirmed);
    }

    [Fact]
    public async Task ExpirePaymentsAsync_WithNoExpiredPayments_ShouldNotThrow()
    {
        // Arrange
        await CreateTestPayment("valid-payment", "buyer-1", DateTime.UtcNow.AddMinutes(30));

        // Act & Assert
        await _service.ExpirePaymentsAsync(); // Should not throw
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreatePaymentAsync_WithInvalidPaymentId_ShouldHandleGracefully(string? paymentId)
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = paymentId!,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "buyer-123"
        };

        var paymentAddress = new PaymentAddress
        {
            Id = 5,
            Address = "test-address",
            CryptoCurrencyId = 1,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _mockAddressService.Setup(x => x.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentAddress);

        // Act & Assert
        if (string.IsNullOrEmpty(paymentId))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => _service.CreatePaymentAsync(request));
        }
    }

    [Fact]
    public async Task CreatePaymentAsync_WithMetadata_ShouldSerializeCorrectly()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["orderId"] = "order-123",
            ["customerId"] = 456,
            ["source"] = "mobile-app",
            ["nested"] = new { key = "value", number = 123 }
        };

        var request = new CreatePaymentRequest
        {
            PaymentId = "metadata-test-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "buyer-123",
            Metadata = metadata
        };

        var paymentAddress = new PaymentAddress
        {
            Id = 6,
            Address = "metadata-test-address",
            CryptoCurrencyId = 1,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _mockAddressService.Setup(x => x.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentAddress);

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        var savedPayment = await _context.CryptoPayments.FirstAsync(p => p.PaymentId == request.PaymentId);
        savedPayment.Metadata.Should().NotBeNull();
        
        var deserializedMetadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(savedPayment.Metadata!);
        deserializedMetadata.Should().ContainKey("orderId");
        deserializedMetadata["orderId"].GetString().Should().Be("order-123");
    }

    private async Task<CryptoPayment> CreateTestPayment(string paymentId, string? buyerId = null, DateTime? expiresAt = null)
    {
        var payment = new CryptoPayment
        {
            PaymentId = paymentId,
            CryptoCurrencyId = 1,
            PaymentAddressId = 1,
            RequestedAmount = 0.001m,
            Status = PaymentStatus.Pending,
            RequiredConfirmations = 6,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(30),
            BuyerId = buyerId ?? "test-buyer"
        };

        _context.CryptoPayments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}