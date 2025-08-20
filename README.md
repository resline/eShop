# eShop Reference Application with Cryptocurrency Payments - Enhanced Fork

A reference .NET application implementing an e-commerce website using a services-based architecture with [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/). This enhanced fork adds comprehensive cryptocurrency payment capabilities, advanced security features, and production-ready resilience patterns.

![eShop Reference Application architecture diagram](img/eshop_architecture.png)

![eShop homepage screenshot](img/eshop_homepage.png)

## Key Features Added

This enhanced fork includes the following major additions to the original eShop reference application:

### 🚀 **Cryptocurrency Payment System**
- **Full Bitcoin & Ethereum Integration**: Complete payment processing with blockchain verification
- **Real-time Transaction Monitoring**: Live payment status updates via SignalR
- **Secure Address Generation**: Dynamic payment address creation with proper key management
- **QR Code Support**: Easy mobile wallet integration for seamless payments

### 🔐 **Enhanced Security Features**
- **HKDF Key Derivation**: Cryptographically secure key generation replacing simple hashing
- **Secure Memory Management**: Protected storage and disposal of sensitive cryptographic data
- **Comprehensive Validation**: FluentValidation integration with detailed input sanitization
- **Rate Limiting & DDoS Protection**: Production-ready middleware for API protection

### 🛡️ **Production-Ready Resilience Patterns**
- **Circuit Breaker Implementation**: Polly-based fault tolerance for external service calls
- **Multi-Tier Caching Strategy**: Redis primary cache with emergency in-memory fallback
- **Automatic Retry Mechanisms**: Exponential backoff with configurable retry policies
- **Graceful Degradation**: Fallback to cached data during service outages

### 📊 **Comprehensive Error Handling**
- **Global Exception Middleware**: Centralized error processing with correlation tracking
- **User-Friendly Error Messages**: Localized error responses with actionable guidance
- **Enhanced Blazor Error Boundaries**: Specialized error handling for crypto payment flows
- **Structured Logging**: Detailed error context for debugging and monitoring

### 🧪 **Extensive Testing Infrastructure**
- **Unit Tests**: Comprehensive coverage for crypto payment services and blockchain operations
- **Integration Tests**: End-to-end testing of payment flows with real blockchain simulation
- **E2E Tests**: Playwright automation for complete user payment scenarios
- **Error Scenario Testing**: Validation of error handling and recovery mechanisms

## Getting Started

This version of eShop is based on .NET 9. 

Previous eShop versions:
* [.NET 8](https://github.com/dotnet/eShop/tree/release/8.0)

### Prerequisites

- Clone the eShop repository: https://github.com/dotnet/eshop
- [Install & start Docker Desktop](https://docs.docker.com/engine/install/)

#### Windows with Visual Studio
- Install [Visual Studio 2022 version 17.10 or newer](https://visualstudio.microsoft.com/vs/).
  - Select the following workloads:
    - `ASP.NET and web development` workload.
    - `.NET Aspire SDK` component in `Individual components`.
    - Optional: `.NET Multi-platform App UI development` to run client apps

Or

- Run the following commands in a Powershell & Terminal running as `Administrator` to automatically configure your environment with the required tools to build and run this application. (Note: A restart is required and included in the script below.)

```powershell
install-Module -Name Microsoft.WinGet.Configuration -AllowPrerelease -AcceptLicense -Force
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
get-WinGetConfiguration -file .\.configurations\vside.dsc.yaml | Invoke-WinGetConfiguration -AcceptConfigurationAgreements
```

Or

- From Dev Home go to `Machine Configuration -> Clone repositories`. Enter the URL for this repository. In the confirmation screen look for the section `Configuration File Detected` and click `Run File`.

#### Mac, Linux, & Windows without Visual Studio
- Install the latest [.NET 9 SDK](https://dot.net/download?cid=eshop)

Or

- Run the following commands in a Powershell & Terminal running as `Administrator` to automatically configuration your environment with the required tools to build and run this application. (Note: A restart is required after running the script below.)

##### Install Visual Studio Code and related extensions
```powershell
install-Module -Name Microsoft.WinGet.Configuration -AllowPrerelease -AcceptLicense  -Force
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
get-WinGetConfiguration -file .\.configurations\vscode.dsc.yaml | Invoke-WinGetConfiguration -AcceptConfigurationAgreements
```

> Note: These commands may require `sudo`

- Optional: Install [Visual Studio Code with C# Dev Kit](https://code.visualstudio.com/docs/csharp/get-started)
- Optional: Install [.NET MAUI Workload](https://learn.microsoft.com/dotnet/maui/get-started/installation?tabs=visual-studio-code)

> Note: When running on Mac with Apple Silicon (M series processor), Rosetta 2 for grpc-tools. 

### Running the solution

> [!WARNING]
> Remember to ensure that Docker is started

* (Windows only) Run the application from Visual Studio:
 - Open the `eShop.Web.slnf` file in Visual Studio
 - Ensure that `eShop.AppHost.csproj` is your startup project
 - Hit Ctrl-F5 to launch Aspire

* Or run the application from your terminal:
```powershell
dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj
```
then look for lines like this in the console output in order to find the URL to open the Aspire dashboard:
```sh
Login to the dashboard at: http://localhost:19888/login?t=uniquelogincodeforyou
```

### Enhanced Services in This Fork

This enhanced version includes additional services that will be automatically started:

- **CryptoPayment.API**: Cryptocurrency payment processing service
- **CryptoPayment.BlockchainServices**: Blockchain monitoring and transaction verification
- **Enhanced WebApp**: Extended Blazor frontend with crypto payment components
- **Real-time SignalR Hubs**: Live payment status updates and notifications

The Aspire dashboard will show all services including the new crypto payment infrastructure. Look for the CryptoPayment.API service to monitor cryptocurrency payment operations.

> You may need to install ASP.NET Core HTTPS development certificates first, and then close all browser tabs. Learn more at https://aka.ms/aspnet/https-trust-dev-cert

### Azure Open AI

When using Azure OpenAI, inside *eShop.AppHost/appsettings.json*, add the following section:

```json
  "ConnectionStrings": {
    "OpenAi": "Endpoint=xxx;Key=xxx;"
  }
```

Replace the values with your own. Then, in the eShop.AppHost *Program.cs*, set this value to **true**

```csharp
bool useOpenAI = false;
```

Here's additional guidance on the [.NET Aspire OpenAI component](https://learn.microsoft.com/dotnet/aspire/azureai/azureai-openai-component?tabs=dotnet-cli). 

## Cryptocurrency Payment System

This enhanced version includes a comprehensive cryptocurrency payment system that integrates seamlessly with the existing eShop architecture.

### Architecture Overview

The crypto payment system consists of the following key components:

- **CryptoPayment.API**: Dedicated microservice for handling cryptocurrency payments
- **CryptoPayment.BlockchainServices**: Library providing blockchain integration and monitoring
- **Enhanced WebApp**: Updated Blazor frontend with crypto payment UI components
- **Integration Events**: Event-driven communication for payment status updates

### Supported Cryptocurrencies

- **Bitcoin (BTC)**: Full support for Bitcoin payments with configurable confirmation requirements
- **Ethereum (ETH)**: Complete Ethereum integration with smart contract compatibility
- **Extensible Design**: Architecture supports easy addition of new cryptocurrencies

### Key Features

#### Payment Processing
- **Dynamic Address Generation**: Unique payment addresses created for each transaction
- **Real-time Monitoring**: Continuous blockchain monitoring for transaction confirmations
- **Automatic Status Updates**: Real-time payment status via SignalR connections
- **Flexible Confirmation Requirements**: Configurable confirmation thresholds per cryptocurrency

#### Security Features
- **Secure Key Management**: HKDF-based key derivation with secure memory handling
- **Address Validation**: Comprehensive validation for all cryptocurrency addresses
- **Transaction Verification**: Multi-layer verification of blockchain transactions
- **Audit Logging**: Complete audit trail for all payment operations

#### User Experience
- **QR Code Generation**: Easy mobile wallet scanning for payments
- **Payment Status Tracking**: Real-time updates on payment progress
- **Error Recovery**: Graceful handling of payment failures with retry mechanisms
- **Multi-language Support**: Localized error messages and payment instructions

### Configuration

To enable cryptocurrency payments, configure the following in your `appsettings.json`:

```json
{
  "CryptoPayment": {
    "Bitcoin": {
      "Network": "testnet",
      "RequiredConfirmations": 3,
      "AddressType": "bech32"
    },
    "Ethereum": {
      "Network": "sepolia",
      "RequiredConfirmations": 12,
      "GasLimit": 21000
    }
  }
}
```

### Use Azure Developer CLI

You can use the [Azure Developer CLI](https://aka.ms/azd) to run this project on Azure with only a few commands. Follow the next instructions:

- Install the latest or update to the latest [Azure Developer CLI (azd)](https://aka.ms/azure-dev/install).
- Log in `azd` (if you haven't done it before) to your Azure account:
```sh
azd auth login
```
- Initialize `azd` from the root of the repo.
```sh
azd init
```
- During init:
  - Select `Use code in the current directory`. Azd will automatically detect the .NET Aspire project.
  - Confirm `.NET (Aspire)` and continue.
  - Select which services to expose to the Internet (exposing `webapp` is enough to test the sample).
  - Finalize the initialization by giving a name to your environment.

- Create Azure resources and deploy the sample by running:
```sh
azd up
```
Notes:
  - The operation takes a few minutes the first time it is ever run for an environment.
  - At the end of the process, `azd` will display the `url` for the webapp. Follow that link to test the sample.
  - You can run `azd up` after saving changes to the sample to re-deploy and update the sample.
  - Report any issues to [azure-dev](https://github.com/Azure/azure-dev/issues) repo.
  - [FAQ and troubleshoot](https://learn.microsoft.com/azure/developer/azure-developer-cli/troubleshoot?tabs=Browser) for azd.

## Security & Resilience Enhancements

This enhanced fork implements production-ready security measures and resilience patterns to ensure robust operation in enterprise environments.

### Security Improvements

#### Cryptographic Security
- **HKDF Key Derivation**: Replaced simple SHA256 concatenation with cryptographically secure HMAC-based Key Derivation Function
- **Secure Memory Management**: Implementation of `SecureString` usage and proper memory clearing for sensitive data
- **Key Rotation**: Automated key rotation service with configurable rotation schedules
- **Forward Secrecy**: Enhanced key derivation provides forward secrecy for all derived keys

#### Input Validation & Sanitization
- **FluentValidation Integration**: Comprehensive input validation across all crypto payment endpoints
- **Address Validation**: Multi-layer cryptocurrency address validation with checksum verification
- **Rate Limiting**: Configurable rate limiting middleware to prevent abuse and DDoS attacks
- **CORS Configuration**: Properly configured Cross-Origin Resource Sharing for secure API access

#### Audit & Monitoring
- **Security Audit Service**: Automated security scanning and vulnerability assessment
- **Comprehensive Logging**: Structured logging with security event correlation
- **Correlation ID Tracking**: End-to-end request tracking for security incident investigation
- **Webhook Security**: HMAC signature verification for all incoming webhook payloads

### Resilience Patterns

#### Circuit Breaker Implementation
- **Polly Integration**: Advanced circuit breaker pattern for external service calls
- **Configurable Thresholds**: 5 failures trigger circuit opening with 1-minute timeout
- **Automatic Recovery**: Self-healing circuit breaker with half-open state testing
- **Fallback Strategies**: Graceful degradation to cached data during service outages

#### Enhanced Caching Strategy
- **Multi-Tier Caching**: Redis primary cache with in-memory emergency fallback
- **Cache Hierarchy**: Primary (5min) → Emergency (2hr stale) → Hardcoded fallback rates
- **Sliding Expiration**: Frequently accessed data remains cached longer
- **Cache Warming**: Proactive cache population to minimize cache misses

#### Error Handling & Recovery
- **Global Exception Middleware**: Centralized error processing with correlation tracking
- **Retry Mechanisms**: Exponential backoff with jitter for transient failures
- **Graceful Degradation**: System continues operating with reduced functionality during partial failures
- **Dead Letter Queues**: Failed message processing with retry and manual intervention capabilities

#### Performance Optimization
- **SignalR Connection Management**: Connection limits and efficient resource utilization
- **Background Services**: Non-blocking async processing for heavy operations
- **Memory Management**: Efficient object disposal and garbage collection optimization
- **Database Connection Pooling**: Optimized connection management for high throughput

### Configuration Examples

#### Circuit Breaker Configuration
```json
{
  "CircuitBreaker": {
    "FailureThreshold": 5,
    "TimeoutDuration": "00:01:00",
    "SamplingDuration": "00:00:30",
    "MinimumThroughput": 5
  }
}
```

#### Rate Limiting Configuration
```json
{
  "RateLimiting": {
    "GlobalPolicy": {
      "PermitLimit": 100,
      "Window": "00:01:00",
      "QueueProcessingOrder": "OldestFirst",
      "QueueLimit": 50
    }
  }
}
```

## Contributing

This is an enhanced fork of the original [.NET eShop reference application](https://github.com/dotnet/eShop) with comprehensive cryptocurrency payment capabilities.

### Original Repository
- **Original Project**: [dotnet/eShop](https://github.com/dotnet/eShop)
- **License**: Same as original (check original repository for license details)
- **Credits**: Microsoft .NET team for the excellent eShop reference architecture

### Enhancements in This Fork
- **Cryptocurrency Payment System**: Complete Bitcoin and Ethereum integration
- **Blockchain Services**: Transaction monitoring and verification
- **Security Improvements**: HKDF key derivation, secure memory management
- **Resilience Patterns**: Circuit breakers, multi-tier caching, automatic fallbacks
- **Comprehensive Testing**: Unit, integration, and E2E tests for crypto features

### Contributing Guidelines
For more information on contributing to this enhanced fork, read [the contribution documentation](./CONTRIBUTING.md) and [the Code of Conduct](CODE-OF-CONDUCT.md).

When contributing:
1. **Maintain Security Standards**: All crypto-related code must follow the established security patterns
2. **Test Coverage**: New features require comprehensive unit, integration, and E2E tests
3. **Documentation**: Update relevant documentation files for any changes
4. **Follow Patterns**: Adhere to existing architectural patterns and coding standards

### Enhanced Testing Suite

This enhanced fork includes comprehensive testing for all cryptocurrency payment features:

#### Unit Tests
```bash
# Crypto payment service tests
dotnet test tests/CryptoPayment.UnitTests/CryptoPayment.UnitTests.csproj

# Blockchain services tests  
dotnet test tests/CryptoPayment.UnitTests/Services/AddressGenerationServiceTests.cs
dotnet test tests/CryptoPayment.UnitTests/Services/CryptoPaymentServiceTests.cs

# Integration event tests
dotnet test tests/CryptoPayment.UnitTests/IntegrationEvents/IntegrationEventTests.cs

# Original eShop unit tests
dotnet test tests/Basket.UnitTests/Basket.UnitTests.csproj
dotnet test tests/Ordering.UnitTests/Ordering.UnitTests.csproj
dotnet test tests/ClientApp.UnitTests/ClientApp.UnitTests.csproj
```

#### Integration Tests
```bash
# Crypto payment API integration tests
dotnet test tests/CryptoPayment.IntegrationTests/CryptoPayment.IntegrationTests.csproj

# Database integration tests
dotnet test tests/CryptoPayment.IntegrationTests/DatabaseIntegrationTests.cs

# Event bus integration tests
dotnet test tests/CryptoPayment.IntegrationTests/EventBusIntegrationTests.cs

# Original eShop functional tests
dotnet test tests/Catalog.FunctionalTests/Catalog.FunctionalTests.csproj
dotnet test tests/Ordering.FunctionalTests/Ordering.FunctionalTests.csproj
```

#### End-to-End Tests
```bash
# Install dependencies
npm install

# Run all Playwright E2E tests
npx playwright test

# Run crypto payment flow tests
npx playwright test e2e/CryptoPaymentFlow.spec.ts

# Run crypto payment error scenario tests
npx playwright test e2e/CryptoPaymentErrorScenarios.spec.ts

# Run original eShop E2E tests
npx playwright test e2e/AddItemTest.spec.ts
```

#### Test Features

- **Crypto Payment Flow Testing**: Complete user journey from product selection to crypto payment completion
- **Error Scenario Validation**: Testing of payment failures, network issues, and recovery mechanisms
- **Real-time Update Testing**: Validation of SignalR-based payment status updates
- **Security Testing**: Validation of input sanitization, rate limiting, and authentication flows
- **Performance Testing**: Load testing for crypto payment endpoints and blockchain monitoring

### Sample data

The sample catalog data is defined in [catalog.json](https://github.com/dotnet/eShop/blob/main/src/Catalog.API/Setup/catalog.json). Those product names, descriptions, and brand names are fictional and were generated using [GPT-35-Turbo](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/chatgpt), and the corresponding [product images](https://github.com/dotnet/eShop/tree/main/src/Catalog.API/Pics) were generated using [DALL·E 3](https://openai.com/dall-e-3).

## Additional Documentation

This enhanced fork includes comprehensive documentation for all new features:

- **[CLAUDE.md](./CLAUDE.md)**: Detailed guidance for AI assistants working with this codebase
- **[SECURITY_IMPROVEMENTS.md](./SECURITY_IMPROVEMENTS.md)**: Complete documentation of security enhancements and fixes
- **[RESILIENCE_PATTERNS_IMPLEMENTATION.md](./RESILIENCE_PATTERNS_IMPLEMENTATION.md)**: Detailed resilience patterns and fault tolerance measures
- **[ERROR_HANDLING.md](./src/CryptoPayment.API/ERROR_HANDLING.md)**: Comprehensive error handling system documentation

## eShop on Azure

For a version of this app configured for deployment on Azure, please view [the eShop on Azure](https://github.com/Azure-Samples/eShopOnAzure) repo.
