namespace CryptoPayment.UnitTests.IntegrationEvents;

public class IntegrationEventTests
{
    [Fact]
    public void CryptoPaymentCreatedIntegrationEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var paymentId = 123;
        var paymentReference = "payment-ref-456";
        var cryptoCurrency = "BTC";
        var paymentAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        var requestedAmount = 0.001m;

        // Act
        var integrationEvent = new CryptoPaymentCreatedIntegrationEvent(
            paymentId, paymentReference, cryptoCurrency, paymentAddress, requestedAmount);

        // Assert
        integrationEvent.PaymentId.Should().Be(paymentId);
        integrationEvent.PaymentReference.Should().Be(paymentReference);
        integrationEvent.CryptoCurrency.Should().Be(cryptoCurrency);
        integrationEvent.PaymentAddress.Should().Be(paymentAddress);
        integrationEvent.RequestedAmount.Should().Be(requestedAmount);
        integrationEvent.Id.Should().NotBeEmpty();
        integrationEvent.CreationDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CryptoPaymentStatusChangedIntegrationEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var paymentId = 123;
        var paymentReference = "payment-ref-456";
        var oldStatus = PaymentStatus.Pending;
        var newStatus = PaymentStatus.Confirmed;
        var transactionHash = "0x123abc456def";
        var receivedAmount = 0.0015m;

        // Act
        var integrationEvent = new CryptoPaymentStatusChangedIntegrationEvent(
            paymentId, paymentReference, oldStatus, newStatus, transactionHash, receivedAmount);

        // Assert
        integrationEvent.PaymentId.Should().Be(paymentId);
        integrationEvent.PaymentReference.Should().Be(paymentReference);
        integrationEvent.OldStatus.Should().Be(oldStatus);
        integrationEvent.NewStatus.Should().Be(newStatus);
        integrationEvent.TransactionHash.Should().Be(transactionHash);
        integrationEvent.ReceivedAmount.Should().Be(receivedAmount);
        integrationEvent.Id.Should().NotBeEmpty();
        integrationEvent.CreationDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CryptoPaymentConfirmedIntegrationEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var paymentId = 123;
        var paymentReference = "payment-ref-456";
        var transactionHash = "0x123abc456def";
        var confirmations = 6;
        var receivedAmount = 0.001m;

        // Act
        var integrationEvent = new CryptoPaymentConfirmedIntegrationEvent(
            paymentId, paymentReference, transactionHash, confirmations, receivedAmount);

        // Assert
        integrationEvent.PaymentId.Should().Be(paymentId);
        integrationEvent.PaymentReference.Should().Be(paymentReference);
        integrationEvent.TransactionHash.Should().Be(transactionHash);
        integrationEvent.Confirmations.Should().Be(confirmations);
        integrationEvent.ReceivedAmount.Should().Be(receivedAmount);
        integrationEvent.Id.Should().NotBeEmpty();
        integrationEvent.CreationDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CryptoPaymentStatusChangedIntegrationEvent_WithNullOrEmptyTransactionHash_ShouldHandleGracefully(string? transactionHash)
    {
        // Act
        var integrationEvent = new CryptoPaymentStatusChangedIntegrationEvent(
            123, "payment-ref", PaymentStatus.Pending, PaymentStatus.Paid, transactionHash, 0.001m);

        // Assert
        integrationEvent.TransactionHash.Should().Be(transactionHash);
    }

    [Fact]
    public void CryptoPaymentStatusChangedIntegrationEvent_WithNullReceivedAmount_ShouldHandleGracefully()
    {
        // Act
        var integrationEvent = new CryptoPaymentStatusChangedIntegrationEvent(
            123, "payment-ref", PaymentStatus.Pending, PaymentStatus.Expired, null, null);

        // Assert
        integrationEvent.ReceivedAmount.Should().BeNull();
    }

    [Fact]
    public void IntegrationEvents_ShouldHaveUniqueIds()
    {
        // Arrange & Act
        var event1 = new CryptoPaymentCreatedIntegrationEvent(1, "ref1", "BTC", "address1", 0.001m);
        var event2 = new CryptoPaymentCreatedIntegrationEvent(2, "ref2", "ETH", "address2", 0.002m);
        var event3 = new CryptoPaymentStatusChangedIntegrationEvent(1, "ref1", PaymentStatus.Pending, PaymentStatus.Paid, "hash", 0.001m);

        // Assert
        var ids = new[] { event1.Id, event2.Id, event3.Id };
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void IntegrationEvents_CreationDate_ShouldBeRecentAndConsistent()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var event1 = new CryptoPaymentCreatedIntegrationEvent(1, "ref1", "BTC", "address1", 0.001m);
        Thread.Sleep(1); // Ensure different timestamps
        var event2 = new CryptoPaymentStatusChangedIntegrationEvent(1, "ref1", PaymentStatus.Pending, PaymentStatus.Paid, "hash", 0.001m);
        
        var after = DateTime.UtcNow;

        // Assert
        event1.CreationDate.Should().BeAfter(before).And.BeBefore(after);
        event2.CreationDate.Should().BeAfter(before).And.BeBefore(after);
        event2.CreationDate.Should().BeAfter(event1.CreationDate);
    }
}