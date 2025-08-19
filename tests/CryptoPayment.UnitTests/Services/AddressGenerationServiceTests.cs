namespace CryptoPayment.UnitTests.Services;

public class AddressGenerationServiceTests : IDisposable
{
    private readonly CryptoPaymentContext _context;
    private readonly Mock<ILogger<AddressGenerationService>> _mockLogger;
    private readonly AddressGenerationService _service;

    public AddressGenerationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CryptoPaymentContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new CryptoPaymentContext(options);
        SeedDatabase();

        _mockLogger = new Mock<ILogger<AddressGenerationService>>();
        _service = new AddressGenerationService(_context, _mockLogger.Object);
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

        _context.PaymentAddresses.AddRange(
            new PaymentAddress
            {
                Id = 1,
                Address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
                CryptoCurrencyId = 1,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            },
            new PaymentAddress
            {
                Id = 2,
                Address = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4",
                CryptoCurrencyId = 1,
                IsUsed = true,
                CreatedAt = DateTime.UtcNow
            },
            new PaymentAddress
            {
                Id = 3,
                Address = "0x742d35Cc6635C0532925a3b8D7386DdA9f2777c4",
                CryptoCurrencyId = 2,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            }
        );

        _context.SaveChanges();
    }

    [Fact]
    public async Task GetUnusedAddressAsync_WithAvailableAddress_ShouldReturnUnusedAddress()
    {
        // Act
        var result = await _service.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin);

        // Assert
        result.Should().NotBeNull();
        result!.Address.Should().Be("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
        result.IsUsed.Should().BeFalse();
        result.CryptoCurrencyId.Should().Be(1);
    }

    [Fact]
    public async Task GetUnusedAddressAsync_WithNoAvailableAddress_ShouldReturnNull()
    {
        // Arrange - Mark all Bitcoin addresses as used
        var bitcoinAddresses = await _context.PaymentAddresses
            .Where(a => a.CryptoCurrencyId == 1)
            .ToListAsync();
        
        foreach (var address in bitcoinAddresses)
        {
            address.IsUsed = true;
        }
        
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUnusedAddressAsync_WithEthereum_ShouldReturnEthereumAddress()
    {
        // Act
        var result = await _service.GetUnusedAddressAsync(CryptoCurrencyType.Ethereum);

        // Assert
        result.Should().NotBeNull();
        result!.Address.Should().Be("0x742d35Cc6635C0532925a3b8D7386DdA9f2777c4");
        result.CryptoCurrencyId.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAddressAsync_WithBitcoin_ShouldGenerateValidBitcoinAddress()
    {
        // Act
        var result = await _service.GenerateAddressAsync(CryptoCurrencyType.Bitcoin);

        // Assert
        result.Should().NotBeNull();
        result.Address.Should().NotBeNullOrEmpty();
        result.CryptoCurrencyId.Should().Be(1);
        result.IsUsed.Should().BeFalse();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));

        // Verify address format (Bitcoin addresses start with 1, 3, or bc1)
        result.Address.Should().MatchRegex(@"^(1|3|bc1)[a-zA-Z0-9]+$");
    }

    [Fact]
    public async Task GenerateAddressAsync_WithEthereum_ShouldGenerateValidEthereumAddress()
    {
        // Act
        var result = await _service.GenerateAddressAsync(CryptoCurrencyType.Ethereum);

        // Assert
        result.Should().NotBeNull();
        result.Address.Should().NotBeNullOrEmpty();
        result.CryptoCurrencyId.Should().Be(2);
        result.IsUsed.Should().BeFalse();

        // Verify address format (Ethereum addresses start with 0x and are 42 characters long)
        result.Address.Should().MatchRegex(@"^0x[a-fA-F0-9]{40}$");
    }

    [Fact]
    public async Task GenerateAddressAsync_ShouldPersistToDatabase()
    {
        // Act
        var result = await _service.GenerateAddressAsync(CryptoCurrencyType.Bitcoin);

        // Assert
        var savedAddress = await _context.PaymentAddresses
            .FirstOrDefaultAsync(a => a.Id == result.Id);
        
        savedAddress.Should().NotBeNull();
        savedAddress!.Address.Should().Be(result.Address);
        savedAddress.CryptoCurrencyId.Should().Be(result.CryptoCurrencyId);
    }

    [Fact]
    public async Task MarkAddressAsUsedAsync_WithValidId_ShouldMarkAddressAsUsed()
    {
        // Arrange
        var addressId = 1; // Unused Bitcoin address

        // Act
        await _service.MarkAddressAsUsedAsync(addressId);

        // Assert
        var address = await _context.PaymentAddresses.FindAsync(addressId);
        address.Should().NotBeNull();
        address!.IsUsed.Should().BeTrue();
        address.UsedAt.Should().NotBeNull();
        address.UsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MarkAddressAsUsedAsync_WithInvalidId_ShouldThrowException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.MarkAddressAsUsedAsync(999));
        
        exception.Message.Should().Contain("Payment address with ID 999 not found");
    }

    [Fact]
    public async Task MarkAddressAsUsedAsync_WithAlreadyUsedAddress_ShouldNotThrow()
    {
        // Arrange
        var addressId = 2; // Already used Bitcoin address

        // Act & Assert
        await _service.MarkAddressAsUsedAsync(addressId); // Should not throw
        
        var address = await _context.PaymentAddresses.FindAsync(addressId);
        address!.IsUsed.Should().BeTrue();
    }

    [Theory]
    [InlineData(CryptoCurrencyType.Bitcoin)]
    [InlineData(CryptoCurrencyType.Ethereum)]
    public async Task GenerateAddressAsync_MultipleGeneration_ShouldCreateUniqueAddresses(CryptoCurrencyType currencyType)
    {
        // Act
        var address1 = await _service.GenerateAddressAsync(currencyType);
        var address2 = await _service.GenerateAddressAsync(currencyType);
        var address3 = await _service.GenerateAddressAsync(currencyType);

        // Assert
        var addresses = new[] { address1.Address, address2.Address, address3.Address };
        addresses.Should().OnlyHaveUniqueItems();
        
        foreach (var address in new[] { address1, address2, address3 })
        {
            address.CryptoCurrencyId.Should().Be((int)currencyType);
            address.IsUsed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GetUnusedAddressAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.GetUnusedAddressAsync(CryptoCurrencyType.Bitcoin, cts.Token));
    }

    [Fact]
    public async Task GenerateAddressAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.GenerateAddressAsync(CryptoCurrencyType.Bitcoin, cts.Token));
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}