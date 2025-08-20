using System.ComponentModel.DataAnnotations;

namespace CryptoPayment.UnitTests.Models;

public class ModelValidationTests
{
    [Fact]
    public void CreatePaymentRequest_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m,
            BuyerId = "buyer-123",
            ExpirationMinutes = 30
        };

        // Act
        var validationResults = ValidateModel(request);

        // Assert
        validationResults.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreatePaymentRequest_WithInvalidPaymentId_ShouldFailValidation(string? paymentId)
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = paymentId!,
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.001m
        };

        // Act
        var validationResults = ValidateModel(request);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.MemberNames.Contains(nameof(CreatePaymentRequest.PaymentId)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.001)]
    [InlineData(-1)]
    public void CreatePaymentRequest_WithInvalidAmount_ShouldFailValidation(decimal amount)
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = amount
        };

        // Act
        var validationResults = ValidateModel(request);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => 
            r.MemberNames.Contains(nameof(CreatePaymentRequest.Amount)) &&
            r.ErrorMessage!.Contains("Amount must be greater than 0"));
    }

    [Fact]
    public void CreatePaymentRequest_WithMinimumValidAmount_ShouldPassValidation()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            PaymentId = "payment-123",
            CryptoCurrency = CryptoCurrencyType.Bitcoin,
            Amount = 0.00000001m // Minimum valid amount
        };

        // Act
        var validationResults = ValidateModel(request);

        // Assert
        validationResults.Should().BeEmpty();
    }

    [Fact]
    public void CryptoCurrency_StaticInstances_ShouldHaveCorrectProperties()
    {
        // Bitcoin tests
        CryptoCurrency.Bitcoin.Id.Should().Be(1);
        CryptoCurrency.Bitcoin.Symbol.Should().Be("BTC");
        CryptoCurrency.Bitcoin.Name.Should().Be("Bitcoin");
        CryptoCurrency.Bitcoin.Decimals.Should().Be(8);
        CryptoCurrency.Bitcoin.NetworkName.Should().Be("Bitcoin");
        CryptoCurrency.Bitcoin.IsActive.Should().BeTrue();

        // Ethereum tests
        CryptoCurrency.Ethereum.Id.Should().Be(2);
        CryptoCurrency.Ethereum.Symbol.Should().Be("ETH");
        CryptoCurrency.Ethereum.Name.Should().Be("Ethereum");
        CryptoCurrency.Ethereum.Decimals.Should().Be(18);
        CryptoCurrency.Ethereum.NetworkName.Should().Be("Ethereum");
        CryptoCurrency.Ethereum.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PaymentStatus_EnumValues_ShouldHaveCorrectNumericValues()
    {
        // Assert
        ((int)PaymentStatus.Pending).Should().Be(1);
        ((int)PaymentStatus.PartiallyPaid).Should().Be(2);
        ((int)PaymentStatus.Paid).Should().Be(3);
        ((int)PaymentStatus.Confirmed).Should().Be(4);
        ((int)PaymentStatus.Failed).Should().Be(5);
        ((int)PaymentStatus.Expired).Should().Be(6);
        ((int)PaymentStatus.Cancelled).Should().Be(7);
    }

    [Fact]
    public void CryptoCurrencyType_EnumValues_ShouldMatchCryptoCurrencyIds()
    {
        // Assert
        ((int)CryptoCurrencyType.Bitcoin).Should().Be(CryptoCurrency.Bitcoin.Id);
        ((int)CryptoCurrencyType.Ethereum).Should().Be(CryptoCurrency.Ethereum.Id);
    }

    [Fact]
    public void CryptoPayment_DefaultValues_ShouldBeSetCorrectly()
    {
        // Arrange & Act
        var payment = new CryptoPayment();

        // Assert
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.RequiredConfirmations.Should().Be(6);
    }

    [Fact]
    public void PaymentResponse_ShouldMapAllRequiredFields()
    {
        // Arrange
        var response = new PaymentResponse
        {
            Id = 1,
            PaymentId = "payment-123",
            CryptoCurrency = "BTC",
            PaymentAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
            RequestedAmount = 0.001m,
            ReceivedAmount = 0.0015m,
            Status = PaymentStatus.Confirmed,
            TransactionHash = "0x123abc456def",
            Confirmations = 6,
            RequiredConfirmations = 6,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddMinutes(30),
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            ErrorMessage = null
        };

        // Assert
        response.Id.Should().Be(1);
        response.PaymentId.Should().Be("payment-123");
        response.CryptoCurrency.Should().Be("BTC");
        response.PaymentAddress.Should().Be("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
        response.RequestedAmount.Should().Be(0.001m);
        response.ReceivedAmount.Should().Be(0.0015m);
        response.Status.Should().Be(PaymentStatus.Confirmed);
        response.TransactionHash.Should().Be("0x123abc456def");
        response.Confirmations.Should().Be(6);
        response.RequiredConfirmations.Should().Be(6);
        response.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void WebhookPayload_ShouldHaveAllRequiredProperties()
    {
        // Arrange
        var payment = new PaymentResponse
        {
            Id = 1,
            PaymentId = "payment-123",
            CryptoCurrency = "BTC",
            PaymentAddress = "test-address",
            RequestedAmount = 0.001m,
            Status = PaymentStatus.Confirmed,
            CreatedAt = DateTime.UtcNow
        };

        var webhook = new WebhookPayload
        {
            EventType = "payment.confirmed",
            Payment = payment,
            Timestamp = DateTime.UtcNow,
            Signature = "signature-123"
        };

        // Assert
        webhook.EventType.Should().Be("payment.confirmed");
        webhook.Payment.Should().Be(payment);
        webhook.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        webhook.Signature.Should().Be("signature-123");
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, validationContext, validationResults, true);
        return validationResults;
    }
}