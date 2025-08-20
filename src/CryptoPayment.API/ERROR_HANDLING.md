# Comprehensive Error Handling System

This document describes the comprehensive error handling system implemented for the crypto payment functionality.

## Overview

The error handling system provides:
- Global exception middleware with correlation ID tracking
- Centralized error categorization and response handling
- Enhanced Blazor error boundaries for crypto components
- Automatic retry mechanisms with circuit breakers
- User-friendly error messages with localization support
- Comprehensive validation with FluentValidation
- Client-side error tracking and reporting

## Components

### 1. Global Exception Middleware (`GlobalExceptionMiddleware.cs`)

**Location**: `/src/CryptoPayment.API/Middleware/GlobalExceptionMiddleware.cs`

**Features**:
- Catches all unhandled exceptions
- Generates correlation IDs for tracking
- Logs structured error information
- Returns user-friendly error responses
- Preserves stack traces only in development
- Integrates with error handling service

**Usage**:
```csharp
app.UseGlobalExceptionHandling();
```

### 2. Centralized Error Handling Service

**Interface**: `IErrorHandlingService.cs`
**Implementation**: `ErrorHandlingService.cs`

**Features**:
- Categorizes errors into predefined types
- Determines retry eligibility
- Provides user-friendly messages
- Records error metrics
- Supports localization

**Error Categories**:
- `Validation`: Input validation errors
- `Security`: Authentication/authorization issues
- `External`: Third-party service failures
- `Transient`: Temporary failures
- `RateLimited`: Rate limit violations
- `Business`: Business rule violations
- `Configuration`: System configuration issues

### 3. Enhanced Error Boundary Component

**Location**: `/src/WebApp/Components/Shared/CryptoErrorBoundary.razor`

**Features**:
- Crypto payment specific error handling
- Automatic retry with progressive delays
- Error logging to backend
- Transaction ID tracking
- Support contact integration
- Payment status checking
- Mobile responsive design

**Usage**:
```razor
<CryptoErrorBoundary TransactionId="@transactionId" OnRetry="HandleRetry">
    <CryptoPaymentComponent />
</CryptoErrorBoundary>
```

### 4. Error Recovery Service

**Interface**: `IErrorRecoveryService.cs`
**Implementation**: `ErrorRecoveryService.cs`

**Features**:
- Automatic retry with exponential backoff
- Circuit breaker pattern for external services
- Graceful degradation with fallback operations
- Compensation action registration
- Configurable retry policies

**Usage**:
```csharp
var result = await errorRecoveryService.ExecuteWithRetryAsync(async () =>
{
    return await externalService.CallAsync();
}, new RetryPolicy { MaxAttempts = 3 });
```

### 5. Validation Middleware

**Location**: `/src/CryptoPayment.API/Middleware/ValidationMiddleware.cs`

**Features**:
- FluentValidation integration
- Custom crypto validation rules
- Client-side validation synchronization
- Structured validation error responses

**Custom Validators**:
- `CreatePaymentRequestValidator`: Payment creation validation
- `PaymentAddressValidator`: Cryptocurrency address validation
- `CryptoCurrencyAmountValidator`: Amount and precision validation

### 6. User-Friendly Exception Classes

**Location**: `/src/CryptoPayment.API/Exceptions/UserFriendlyExceptions.cs`

**Available Exceptions**:
- `CryptoPaymentBusinessException`: Business rule violations
- `InsufficientFundsException`: Insufficient cryptocurrency funds
- `InvalidAddressException`: Invalid wallet addresses
- `TransactionTimeoutException`: Transaction timeout scenarios
- `PaymentProcessingException`: Payment processing failures
- `RateLimitExceededException`: Rate limiting scenarios
- `ExternalServiceException`: External service failures
- `ValidationException`: Validation errors with field details

## Configuration

### Service Registration

```csharp
// In Program.cs
builder.Services.AddErrorHandling();

// In application pipeline
app.UseErrorHandling();
```

### Error Handling Options

```json
{
  "ErrorHandling": {
    "EnableDetailedErrors": false,
    "MaxRetryAttempts": 3,
    "CircuitBreakerThreshold": 5,
    "DefaultRetryDelay": "00:00:01"
  }
}
```

### Localization

Error messages are localized using resource files:
- `Resources/ErrorMessages.resx` (English)
- `Resources/ErrorMessages.es.resx` (Spanish - if needed)
- `Resources/ErrorMessages.fr.resx` (French - if needed)

## Error Response Format

All errors follow the RFC 7807 Problem Details format:

```json
{
  "type": "https://docs.cryptopayment.api/errors/validation",
  "title": "Validation Error",
  "status": 400,
  "detail": "The provided information is invalid.",
  "instance": "/api/crypto-payment/create",
  "correlationId": "abc123",
  "errorId": "def456",
  "timestamp": "2024-01-01T12:00:00Z",
  "retryable": false,
  "helpLink": "https://docs.cryptopayment.api/validation-errors"
}
```

## Monitoring and Metrics

### Error Metrics

The system records the following metrics:
- Error count by category
- Error count by type
- Retry success/failure rates
- Circuit breaker state changes
- Response times

### Logging Structure

All errors are logged with structured data:
```csharp
{
  "CorrelationId": "abc123",
  "ErrorCategory": "External",
  "ErrorType": "HttpRequestException",
  "UserId": "user123",
  "ClientIp": "192.168.1.1",
  "IsRetryable": true,
  "Component": "CryptoPaymentService"
}
```

## Best Practices

### 1. Exception Handling

- Use user-friendly exceptions for business scenarios
- Always include correlation IDs
- Log errors with appropriate levels
- Don't expose sensitive information

### 2. Retry Logic

- Only retry transient errors
- Use exponential backoff with jitter
- Set maximum retry limits
- Consider circuit breakers for external services

### 3. User Experience

- Provide clear, actionable error messages
- Offer retry options when appropriate
- Include contact support for critical errors
- Show transaction IDs for payment errors

### 4. Security

- Never expose stack traces to users
- Sanitize error messages
- Log security events appropriately
- Rate limit error reporting endpoints

## Client-Side Error Handling

### Error Boundary Usage

```razor
<CryptoErrorBoundary 
    IsFullPage="true"
    ShowContactSupport="true"
    TransactionId="@Model.TransactionId"
    OnError="HandleCryptoError"
    OnSupportRequest="ContactSupport">
    
    <CryptoPaymentFlow />
</CryptoErrorBoundary>
```

### Error Tracking

Client errors are automatically reported to:
```
POST /api/error-tracking/client-errors
```

With payload:
```json
{
  "correlationId": "abc123",
  "transactionId": "tx456",
  "errorType": "HttpRequestException",
  "errorMessage": "Network error",
  "component": "CryptoPaymentFlow",
  "url": "https://app.com/checkout",
  "userAgent": "Mozilla/5.0..."
}
```

## Testing Error Handling

### Unit Tests

Test error scenarios for:
- Each exception type
- Retry logic
- Circuit breaker behavior
- Validation rules

### Integration Tests

Test end-to-end error flows:
- API error responses
- Error boundary behavior
- Error tracking functionality

### Example Test

```csharp
[Test]
public async Task Should_Retry_Transient_Errors()
{
    // Arrange
    var mockService = new Mock<IExternalService>();
    mockService.SetupSequence(x => x.CallAsync())
           .ThrowsAsync(new TaskCanceledException())
           .ReturnsAsync("success");

    // Act
    var result = await errorRecoveryService.ExecuteWithRetryAsync(
        () => mockService.Object.CallAsync());

    // Assert
    Assert.AreEqual("success", result);
    mockService.Verify(x => x.CallAsync(), Times.Exactly(2));
}
```

## Troubleshooting

### Common Issues

1. **High Error Rates**: Check external service health
2. **Circuit Breaker Open**: Investigate service failures
3. **Validation Failures**: Review input validation rules
4. **Missing Correlation IDs**: Ensure middleware is registered

### Debugging

1. Check correlation IDs in logs
2. Review error categories and retry attempts
3. Monitor circuit breaker states
4. Verify error message localization

## Security Considerations

1. **Information Disclosure**: Error messages don't expose internal details
2. **Rate Limiting**: Error reporting endpoints are rate limited
3. **Input Validation**: All inputs are validated before processing
4. **Audit Trail**: All errors are logged with user context
5. **Correlation Tracking**: Errors can be traced across service boundaries

## Performance Impact

The error handling system is designed for minimal performance impact:
- Structured logging uses efficient serialization
- Error categorization uses cached mappings
- Circuit breakers prevent cascading failures
- Retry policies use optimized delays

## Future Enhancements

Planned improvements:
1. Machine learning for error pattern detection
2. Automated error categorization refinement
3. Enhanced client-side error recovery
4. Integration with external monitoring systems
5. Advanced correlation tracking across microservices