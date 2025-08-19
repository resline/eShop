# CryptoPayment BlockchainServices

A comprehensive .NET library for integrating Bitcoin and Ethereum blockchain operations into the eShop application. This library provides unified interfaces for cryptocurrency payment processing, transaction monitoring, and address management.

## Features

### Supported Cryptocurrencies
- **Bitcoin (BTC)** - Full testnet and mainnet support
- **Ethereum (ETH)** - Native ETH transactions
- **ERC-20 Tokens** - USDT, USDC, and other standard tokens

### Core Capabilities
- **Address Generation** - HD wallet support for Bitcoin, secure key generation for Ethereum
- **Transaction Processing** - Send and receive cryptocurrency transactions
- **Real-time Monitoring** - WebSocket and polling-based transaction monitoring
- **Security Features** - Encrypted key storage, address validation, transaction signing
- **Provider Integration** - Coinbase Commerce, BitPay, Infura/Alchemy support

## Architecture

### Service Layer
- `IBitcoinService` - Bitcoin-specific operations
- `IEthereumService` - Ethereum and token operations
- `IBlockchainServiceFactory` - Service resolution and management

### Security Layer
- `IKeyManager` - Private key generation, encryption, and signing
- `IAddressValidator` - Address format validation for all supported currencies
- `IKeyStorage` - Secure key storage with multiple backend options

### Monitoring Layer
- `TransactionMonitorService` - Background service for transaction status tracking
- WebSocket subscriptions for real-time updates
- Polling fallback for reliable monitoring

## Configuration

### Basic Setup

```json
{
  "Blockchain": {
    "Bitcoin": {
      "RpcEndpoint": "http://localhost:18332",
      "RpcUsername": "bitcoin",
      "RpcPassword": "bitcoin",
      "Network": "Testnet",
      "ConfirmationsRequired": 3,
      "MinimumAmount": 0.0001
    },
    "Ethereum": {
      "RpcEndpoint": "https://sepolia.infura.io/v3/YOUR_PROJECT_ID",
      "Network": "Testnet",
      "ConfirmationsRequired": 12,
      "MinimumAmount": 0.001,
      "TokenContracts": {
        "USDT": "0x110a13FC3efE6A245B50102D2d79B3E76125Ae83",
        "USDC": "0x07865c6E87B9F70255377e024ace6630C1Eaa37F"
      }
    }
  }
}
```

### Service Registration

```csharp
services.AddBlockchainServices(configuration);
```

## Usage Examples

### Generate Payment Address

```csharp
var serviceFactory = serviceProvider.GetService<IBlockchainServiceFactory>();
var bitcoinService = serviceFactory.GetBitcoinService();

var address = await bitcoinService.GenerateAddressAsync("Order #12345");
Console.WriteLine($"Bitcoin address: {address.Address}");
```

### Send Transaction

```csharp
var ethereumService = serviceFactory.GetEthereumService();

var request = new TransactionRequest
{
    ToAddress = "0x742d35Cc6635C0532925a3b8D400E7BDd13C2928",
    Amount = 0.1m,
    Currency = "ETH",
    PrivateKey = "your-private-key"
};

var result = await ethereumService.SendTransactionAsync(request);
if (result.Success)
{
    Console.WriteLine($"Transaction sent: {result.TransactionHash}");
}
```

### Monitor Transactions

```csharp
var monitor = serviceProvider.GetService<TransactionMonitorService>();

monitor.TransactionConfirmed += (sender, args) =>
{
    Console.WriteLine($"Transaction {args.Transaction.TransactionHash} confirmed!");
};

monitor.AddTransaction("0x123...", "ETH", "0x456...", 0.1m);
```

### Token Operations

```csharp
var ethereumService = serviceFactory.GetEthereumService();

// Get USDT balance
var balance = await ethereumService.GetTokenBalanceAsync(
    "0x742d35Cc6635C0532925a3b8D400E7BDd13C2928",
    "0x110a13FC3efE6A245B50102D2d79B3E76125Ae83"
);

// Send USDT tokens
var tokenRequest = new TransactionRequest
{
    ToAddress = "0x742d35Cc6635C0532925a3b8D400E7BDd13C2928",
    Amount = 100m,
    Currency = "USDT",
    TokenContract = "0x110a13FC3efE6A245B50102D2d79B3E76125Ae83",
    PrivateKey = "your-private-key"
};

var result = await ethereumService.SendTransactionAsync(tokenRequest);
```

## Security Considerations

### Key Management
- Private keys are encrypted using AES-256 encryption
- Support for multiple storage backends (In-Memory, File, Azure Key Vault)
- Secure key derivation using PBKDF2

### Address Validation
- Comprehensive validation for Bitcoin addresses (Legacy, SegWit, Bech32)
- Ethereum address checksum validation
- Token contract address verification

### Network Security
- HTTPS-only connections in production
- Request signing for provider APIs
- Webhook signature validation

## Testing with Testnets

### Bitcoin Testnet
- Uses Bitcoin testnet network by default
- Requires local Bitcoin Core node with testnet configuration
- Test coins available from testnet faucets

### Ethereum Sepolia
- Configured for Sepolia testnet
- Free testnet ETH available from faucets
- Testnet token contracts for USDT/USDC testing

## Provider Integration

### Coinbase Commerce
```csharp
var coinbaseClient = serviceProvider.GetService<ICoinbaseCommerceClient>();

var charge = await coinbaseClient.CreateChargeAsync(new CoinbaseChargeRequest
{
    Name = "Test Product",
    Description = "Test payment",
    LocalPrice = new CoinbaseMoney { Amount = "100.00", Currency = "USD" }
});
```

### BitPay
```csharp
var bitpayClient = serviceProvider.GetService<IBitPayClient>();

var invoice = await bitpayClient.CreateInvoiceAsync(new BitPayInvoiceRequest
{
    Price = 100.00m,
    Currency = "USD",
    ItemDesc = "Test payment"
});
```

## Error Handling

The library provides comprehensive exception handling:

- `BlockchainException` - Base exception for blockchain operations
- `InvalidAddressException` - Invalid address format
- `InsufficientFundsException` - Insufficient balance for transaction
- `TransactionNotFoundException` - Transaction not found
- `NetworkConnectionException` - Network connectivity issues
- `KeyManagementException` - Key storage and cryptographic operations

## Logging

Built-in logging support with configurable levels:

```json
{
  "Logging": {
    "LogLevel": {
      "CryptoPayment.BlockchainServices": "Debug"
    }
  }
}
```

## Performance Considerations

- Connection pooling for RPC calls
- Async/await patterns throughout
- Efficient transaction monitoring with batched operations
- WebSocket connections for real-time updates

## Dependencies

- Nethereum.Web3 4.26.0 - Ethereum blockchain interaction
- NBitcoin 7.0.39 - Bitcoin blockchain interaction
- RestSharp 115.1.3 - HTTP client for provider APIs
- Microsoft.Extensions.* - Configuration, DI, Logging, Hosting

## License

This project is part of the eShop reference application and follows the same licensing terms.