# Crypto Payment API Security Implementation

## Overview

This document outlines the security measures implemented in the Crypto Payment API to protect sensitive information, particularly private keys and API credentials.

## Key Security Features

### 1. Environment Variable Configuration

All sensitive configuration values have been moved from `appsettings.json` to environment variables:

- `CRYPTO_PAYMENT_MASTER_KEY`: Master encryption key for deriving other keys
- `CRYPTO_PAYMENT_ENCRYPTION_KEY`: Direct encryption key (fallback)
- `INFURA_PROJECT_ID`: Infura API project ID
- `ALCHEMY_API_KEY`: Alchemy API key
- `COINBASE_COMMERCE_API_KEY`: Coinbase Commerce API key
- `COINBASE_COMMERCE_WEBHOOK_SECRET`: Coinbase webhook validation secret
- `BITPAY_API_TOKEN`: BitPay API token
- `BITPAY_PRIVATE_KEY`: BitPay private key
- `BITPAY_WEBHOOK_SECRET`: BitPay webhook validation secret

### 2. Key Management Security

#### KeyVaultConfiguration
- Centralized secure key retrieval from environment variables
- Support for placeholder resolution (`${VAR_NAME}` format)
- Master key derivation for different purposes
- Secure key caching with proper lifecycle management

#### KeyManager Security Improvements
- Removed hardcoded encryption keys
- Implements key derivation from master key using purpose-specific salts
- Private keys are never stored in plain text
- Proper key validation before storage/usage

### 3. Model Security

#### PaymentAddress Model
- `PrivateKey` field replaced with `PrivateKeyId`
- Private keys stored separately with encryption
- Only references/IDs exposed in models, never actual keys

#### TransactionRequest Model
- `PrivateKey` field replaced with `PrivateKeyId`
- Secure reference-based key management

### 4. Webhook Security

#### WebhookValidationService
- HMAC-SHA256 signature validation
- Timestamp validation with configurable tolerance (default: 5 minutes)
- Support for multiple signature formats
- Secure string comparison to prevent timing attacks
- Webhook secret rotation capability
- Rate limiting specific to webhook endpoints

#### Features:
- Provider-specific secret management (Coinbase, BitPay, etc.)
- Payload format validation
- Secure secret caching
- Comprehensive logging without exposing sensitive data

### 5. Rate Limiting

#### Rate Limiting Policies:
- **Global**: 100 requests/minute per IP
- **API**: 50 requests/minute per client
- **Webhook**: 20 requests/minute per client
- **Payment**: 10 tokens with 5 replenishment/minute (Token Bucket)
- **Admin**: 10 requests per 5 minutes
- **Burst**: 30 requests/minute with sliding window

#### Client Identification:
1. Authenticated user ID (preferred)
2. API key from headers
3. Client IP address (with proxy header support)

#### Features:
- Custom rejection responses with Retry-After headers
- Comprehensive logging of rate limit violations
- Support for different rate limiting algorithms

## Environment Setup

### Required Environment Variables

```bash
# Master encryption key (32+ characters)
CRYPTO_PAYMENT_MASTER_KEY="your-master-encryption-key-here-32-chars-minimum"

# Blockchain provider credentials
INFURA_PROJECT_ID="your-infura-project-id"
ALCHEMY_API_KEY="your-alchemy-api-key"

# Payment provider credentials
COINBASE_COMMERCE_API_KEY="your-coinbase-api-key"
COINBASE_COMMERCE_WEBHOOK_SECRET="your-coinbase-webhook-secret"
BITPAY_API_TOKEN="your-bitpay-api-token"
BITPAY_PRIVATE_KEY="your-bitpay-private-key"
BITPAY_WEBHOOK_SECRET="your-bitpay-webhook-secret"
```

### Docker Environment

```dockerfile
ENV CRYPTO_PAYMENT_MASTER_KEY="your-master-key"
ENV INFURA_PROJECT_ID="your-infura-id"
# ... other environment variables
```

### Kubernetes Secrets

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: crypto-payment-secrets
type: Opaque
data:
  CRYPTO_PAYMENT_MASTER_KEY: <base64-encoded-key>
  INFURA_PROJECT_ID: <base64-encoded-id>
  # ... other secrets
```

## Security Best Practices

### 1. Key Management
- Never store private keys in source code or configuration files
- Use environment variables or secure key vaults (Azure Key Vault, AWS Secrets Manager)
- Implement key rotation procedures
- Use derived keys for different purposes

### 2. API Security
- Implement rate limiting to prevent abuse
- Use HTTPS for all communications
- Validate all webhook signatures
- Implement proper authentication and authorization
- Log security events without exposing sensitive data

### 3. Monitoring and Alerting
- Monitor for unusual rate limiting patterns
- Alert on webhook signature validation failures
- Monitor for unauthorized API access attempts
- Implement proper audit logging

### 4. Development Guidelines
- Never commit secrets to version control
- Use different keys for development, staging, and production
- Implement proper error handling that doesn't leak sensitive information
- Regular security reviews and penetration testing

## Webhook Security Implementation

### Signature Validation
```csharp
var isValid = await _webhookValidationService.ValidateSignatureAsync(
    payload, 
    signature, 
    providerId: "coinbase");
```

### Timestamp Validation
```csharp
var isValidTimestamp = await _webhookValidationService.ValidateTimestampAsync(
    timestampHeader, 
    tolerance: TimeSpan.FromMinutes(5));
```

### Secret Rotation
```csharp
await _webhookValidationService.RotateWebhookSecretAsync(
    "coinbase", 
    newSecret);
```

## Rate Limiting Usage

### Applying Rate Limits to Endpoints
```csharp
// In minimal API registration
app.MapPost("/api/payments", CreatePayment)
   .RequireRateLimiting("payment")
   .RequireAuthorization();

app.MapPost("/api/webhooks", HandleWebhook)
   .RequireRateLimiting("webhook");
```

## Compliance and Auditing

### PCI DSS Considerations
- Secure key storage and transmission
- Regular security assessments
- Access control and monitoring
- Secure development practices

### SOC 2 Type II
- Comprehensive logging and monitoring
- Access controls and authentication
- Data encryption at rest and in transit
- Regular security training and awareness

## Incident Response

### Security Incident Procedures
1. Immediately rotate compromised keys
2. Review access logs for unauthorized access
3. Implement additional monitoring
4. Update security measures as needed
5. Document lessons learned

### Emergency Contacts
- Security team: security@company.com
- DevOps team: devops@company.com
- Management escalation: management@company.com

## Regular Security Tasks

### Weekly
- Review rate limiting logs
- Check for webhook validation failures
- Monitor unusual API usage patterns

### Monthly
- Rotate webhook secrets
- Review and update environment variables
- Security assessment review

### Quarterly
- Full security audit
- Penetration testing
- Update security documentation
- Security training refresh