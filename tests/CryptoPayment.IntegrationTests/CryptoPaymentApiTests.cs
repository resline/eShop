using System.Net;

namespace CryptoPayment.IntegrationTests;

public class CryptoPaymentApiTests : IClassFixture<CryptoPaymentApiFixture>
{
    private readonly CryptoPaymentApiFixture _fixture;

    public CryptoPaymentApiTests(CryptoPaymentApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreatePayment_WithValidData_ShouldReturnCreatedPayment()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = $"integration-test-{Guid.NewGuid()}",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer",
            ExpirationMinutes = 30,
            Metadata = new Dictionary<string, object>
            {
                ["test"] = "integration-test",
                ["orderId"] = "order-123"
            }
        };

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        result.Should().NotBeNull();
        result!.PaymentId.Should().Be(request.PaymentId);
        result.CryptoCurrency.Should().Be("BTC");
        result.RequestedAmount.Should().Be(request.Amount);
        result.Status.Should().Be(PaymentStatus.Pending);
        result.PaymentAddress.Should().NotBeNullOrEmpty();
        result.RequiredConfirmations.Should().Be(6);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreatePayment_WithEthereum_ShouldSetCorrectConfirmations()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = $"eth-test-{Guid.NewGuid()}",
            CryptoCurrency = CryptoCurrencyType.Ethereum,
            Amount = 0.1m,
            BuyerId = "test-buyer"
        };

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        result!.CryptoCurrency.Should().Be("ETH");
        result.RequiredConfirmations.Should().Be(12);
    }

    [Fact]
    public async Task CreatePayment_WithDuplicatePaymentId_ShouldReturnExistingPayment()
    {
        // Arrange
        var paymentId = $"duplicate-test-{Guid.NewGuid()}";
        
        var request1 = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer"
        };

        var request2 = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.002m, // Different amount
            BuyerId = "different-buyer"
        };

        // Act
        var response1 = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request1);
        var response2 = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request2);

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result1 = await response1.Content.ReadFromJsonAsync<PaymentResponse>();
        var result2 = await response2.Content.ReadFromJsonAsync<PaymentResponse>();
        
        result1!.PaymentId.Should().Be(paymentId);
        result2!.PaymentId.Should().Be(paymentId);
        result1.Id.Should().Be(result2.Id);
        result1.RequestedAmount.Should().Be(0.001m); // Original amount, not updated
    }

    [Fact]
    public async Task CreatePayment_WithInvalidData_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "", // Invalid
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = -0.001m, // Invalid
            BuyerId = "test-buyer"
        };

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPaymentStatus_WithValidPaymentId_ShouldReturnPayment()
    {
        // Arrange
        var paymentId = $"status-test-{Guid.NewGuid()}";
        var createRequest = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer"
        };

        await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", createRequest);

        // Act
        var response = await _fixture.HttpClient.GetAsync($"/api/crypto-payments/{paymentId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        result.Should().NotBeNull();
        result!.PaymentId.Should().Be(paymentId);
    }

    [Fact]
    public async Task GetPaymentStatus_WithInvalidPaymentId_ShouldReturnNotFound()
    {
        // Act
        var response = await _fixture.HttpClient.GetAsync("/api/crypto-payments/non-existent-payment/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePaymentStatus_WithValidData_ShouldUpdateSuccessfully()
    {
        // Arrange
        var paymentId = $"update-test-{Guid.NewGuid()}";
        var createRequest = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer"
        };

        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", createRequest);
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentResponse>();

        var updateRequest = new
        {
            Status = PaymentStatus.Paid,
            TransactionHash = "0x123abc456def",
            ReceivedAmount = 0.0015m,
            Confirmations = 3
        };

        // Act
        var response = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/crypto-payments/{createdPayment!.Id}/status", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        result!.Status.Should().Be(PaymentStatus.Paid);
        result.TransactionHash.Should().Be("0x123abc456def");
        result.ReceivedAmount.Should().Be(0.0015m);
        result.Confirmations.Should().Be(3);
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdatePaymentStatus_WithInvalidPaymentId_ShouldReturnNotFound()
    {
        // Arrange
        var updateRequest = new
        {
            Status = PaymentStatus.Paid
        };

        // Act
        var response = await _fixture.HttpClient.PutAsJsonAsync("/api/crypto-payments/999/status", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPaymentsByBuyerId_WithValidBuyerId_ShouldReturnPayments()
    {
        // Arrange
        var buyerId = $"buyer-{Guid.NewGuid()}";
        var paymentId1 = $"payment-1-{Guid.NewGuid()}";
        var paymentId2 = $"payment-2-{Guid.NewGuid()}";

        var request1 = new CreatePaymentRequest
        {
            PaymentId = paymentId1,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = buyerId
        };

        var request2 = new CreatePaymentRequest
        {
            PaymentId = paymentId2,
            CryptoCurrency = CryptoCurrencyType.Ethereum,
            Amount = 0.1m,
            BuyerId = buyerId
        };

        await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request1);
        await Task.Delay(100); // Ensure different creation times
        await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request2);

        // Act
        var response = await _fixture.HttpClient.GetAsync($"/api/crypto-payments/buyer/{buyerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<PaymentResponse>>();
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(p => p.CreatedAt);
        result.Should().OnlyContain(p => new[] { paymentId1, paymentId2 }.Contains(p.PaymentId));
    }

    [Fact]
    public async Task GetPaymentsByBuyerId_WithNonExistentBuyerId_ShouldReturnEmptyList()
    {
        // Act
        var response = await _fixture.HttpClient.GetAsync("/api/crypto-payments/buyer/non-existent-buyer");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<PaymentResponse>>();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await _fixture.HttpClient.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task CreatePayment_ShouldPersistToDatabase()
    {
        // Arrange
        var paymentId = $"db-test-{Guid.NewGuid()}";
        var request = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer",
            Metadata = new Dictionary<string, object> { ["test"] = "value" }
        };

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify in database
        await _fixture.ExecuteDbContextAsync(async context =>
        {
            var payment = await context.CryptoPayments
                .Include(p => p.CryptoCurrency)
                .Include(p => p.PaymentAddress)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

            payment.Should().NotBeNull();
            payment!.PaymentId.Should().Be(paymentId);
            payment.CryptoCurrency.Symbol.Should().Be("BTC");
            payment.PaymentAddress.Should().NotBeNull();
            payment.Metadata.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task ConcurrentPaymentCreation_ShouldHandleGracefully()
    {
        // Arrange
        var paymentId = $"concurrent-test-{Guid.NewGuid()}";
        var request = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer"
        };

        var tasks = new List<Task<HttpResponseMessage>>();
        
        // Act - Send multiple concurrent requests with same payment ID
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        var successfulResponses = responses.Where(r => r.IsSuccessStatusCode).ToList();
        successfulResponses.Should().HaveCount(5); // All should succeed (first creates, others return existing)

        var createdResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var okResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();

        createdResponses.Should().HaveCount(1); // Only one should actually create
        okResponses.Should().HaveCount(4); // Others should return existing
    }
}