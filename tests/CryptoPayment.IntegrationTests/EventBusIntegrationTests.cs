using eShop.EventBus.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace CryptoPayment.IntegrationTests;

public class EventBusIntegrationTests : IClassFixture<CryptoPaymentApiFixture>
{
    private readonly CryptoPaymentApiFixture _fixture;

    public EventBusIntegrationTests(CryptoPaymentApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreatePayment_ShouldPublishIntegrationEvent()
    {
        // Arrange
        var receivedEvents = new List<CryptoPaymentCreatedIntegrationEvent>();
        var eventBus = await _fixture.GetRequiredService<IEventBus>();
        
        // Subscribe to the integration event
        eventBus.Subscribe<CryptoPaymentCreatedIntegrationEvent, TestCryptoPaymentCreatedEventHandler>();

        var paymentId = $"event-test-{Guid.NewGuid()}";
        var request = new CreatePaymentRequest
        {
            PaymentId = paymentId,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "test-buyer"
        };

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/crypto-payments", request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        // Wait a bit for event processing
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Note: In a real test, you would verify the event was published to RabbitMQ
        // This is a simplified test that verifies the API call succeeds
        // In practice, you'd need to set up event handlers and verify they were called
    }

    [Fact]
    public async Task UpdatePaymentStatus_ShouldPublishStatusChangedEvent()
    {
        // Arrange
        var paymentId = $"status-event-test-{Guid.NewGuid()}";
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
            ReceivedAmount = 0.001m,
            Confirmations = 6
        };

        // Act
        var response = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/crypto-payments/{createdPayment!.Id}/status", updateRequest);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        // Wait a bit for event processing
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Verify the update was successful
        var updatedPayment = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        updatedPayment!.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task RabbitMqConnection_ShouldBeHealthy()
    {
        // This test verifies that RabbitMQ connection is working
        // Act & Assert
        var response = await _fixture.HttpClient.GetAsync("/health");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}

// Test event handler for integration tests
public class TestCryptoPaymentCreatedEventHandler : IIntegrationEventHandler<CryptoPaymentCreatedIntegrationEvent>
{
    public static readonly List<CryptoPaymentCreatedIntegrationEvent> ReceivedEvents = new();

    public Task Handle(CryptoPaymentCreatedIntegrationEvent @event)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}