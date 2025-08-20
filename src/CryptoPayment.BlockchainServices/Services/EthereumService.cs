using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Contracts.Standards.ERC20;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketClient;
using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Models;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.BlockchainServices.Services;

public class EthereumService : IEthereumService
{
    private readonly ILogger<EthereumService> _logger;
    private readonly EthereumOptions _options;
    private readonly IAddressValidator _addressValidator;
    private readonly IKeyManager _keyManager;
    private readonly Web3 _web3;
    private readonly Dictionary<string, Erc20Service> _tokenServices;

    public string SupportedCurrency => "ETH";
    public bool SupportsTokens => true;

    public EthereumService(
        ILogger<EthereumService> logger,
        IOptions<BlockchainOptions> options,
        IAddressValidator addressValidator,
        IKeyManager keyManager)
    {
        _logger = logger;
        _options = options.Value.Ethereum;
        _addressValidator = addressValidator;
        _keyManager = keyManager;
        
        _web3 = new Web3(_options.RpcEndpoint);
        _tokenServices = new Dictionary<string, Erc20Service>();
        
        // Initialize token services
        foreach (var token in _options.TokenContracts)
        {
            _tokenServices[token.Key] = new Erc20Service(_web3, token.Value);
        }
        
        _logger.LogInformation("Ethereum service initialized for {Network}", _options.Network);
    }

    public async Task<PaymentAddress> GenerateAddressAsync(string? label = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var privateKey = await _keyManager.GeneratePrivateKeyAsync(cancellationToken);
            var account = new Account(privateKey);
            var address = account.Address;
            var publicKey = account.PublicKey;

            var paymentAddress = new PaymentAddress
            {
                Address = address,
                Currency = SupportedCurrency,
                PrivateKey = await _keyManager.EncryptPrivateKeyAsync(privateKey, cancellationToken),
                PublicKey = publicKey,
                DerivationIndex = 0, // Ethereum doesn't use HD wallets in the same way
                Label = label,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Generated Ethereum address {Address} with label {Label}", address, label);
            return paymentAddress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Ethereum address");
            throw new InvalidOperationException("Failed to generate Ethereum address", ex);
        }
    }

    public async Task<AddressBalance> GetBalanceAsync(string address, string? tokenContract = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_addressValidator.IsValidEthereumAddress(address))
            {
                throw new ArgumentException($"Invalid Ethereum address: {address}");
            }

            decimal balance;
            string currency;

            if (string.IsNullOrEmpty(tokenContract))
            {
                // Get ETH balance
                var balanceWei = await _web3.Eth.GetBalance.SendRequestAsync(address);
                balance = Web3.Convert.FromWei(balanceWei.Value);
                currency = SupportedCurrency;
            }
            else
            {
                // Get token balance
                balance = await GetTokenBalanceAsync(address, tokenContract, cancellationToken);
                currency = GetTokenSymbol(tokenContract);
            }

            return new AddressBalance
            {
                Address = address,
                Balance = balance,
                Currency = currency,
                TokenContract = tokenContract,
                ConfirmedBalance = balance, // Ethereum doesn't have unconfirmed like Bitcoin
                UnconfirmedBalance = 0,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get balance for address {Address}", address);
            throw new InvalidOperationException($"Failed to get balance for address {address}", ex);
        }
    }

    public async Task<decimal> GetTokenBalanceAsync(string address, string tokenContract, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenService = new Erc20Service(_web3, tokenContract);
            var balance = await tokenService.BalanceOfQueryAsync(address);
            var decimals = await tokenService.DecimalsQueryAsync();
            
            return Web3.Convert.FromWei(balance, decimals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get token balance for {Address} on contract {Contract}", address, tokenContract);
            throw new InvalidOperationException($"Failed to get token balance", ex);
        }
    }

    public async Task<Transaction?> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var transaction = await _web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionHash);
            if (transaction == null) return null;

            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash);
            var block = receipt?.BlockNumber != null ? 
                await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(receipt.BlockNumber) : null;

            var confirmations = await GetConfirmationsAsync(transactionHash, cancellationToken);
            var status = GetTransactionStatus(confirmations, receipt?.Status?.Value == 1);

            return new Transaction
            {
                Id = transactionHash,
                Hash = transactionHash,
                FromAddress = transaction.From ?? string.Empty,
                ToAddress = transaction.To ?? string.Empty,
                Amount = Web3.Convert.FromWei(transaction.Value.Value),
                Currency = SupportedCurrency,
                Status = status,
                Confirmations = confirmations,
                RequiredConfirmations = _options.ConfirmationsRequired,
                Fee = CalculateTransactionFee(transaction, receipt),
                CreatedAt = block?.Timestamp != null ? DateTimeOffset.FromUnixTimeSeconds((long)block.Timestamp.Value).DateTime : DateTime.UtcNow,
                ConfirmedAt = status == TransactionStatus.Confirmed && block?.Timestamp != null ? 
                    DateTimeOffset.FromUnixTimeSeconds((long)block.Timestamp.Value).DateTime : null,
                BlockHash = receipt?.BlockHash,
                BlockNumber = (long?)receipt?.BlockNumber?.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transaction {Hash}", transactionHash);
            return null;
        }
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(string address, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var transactions = new List<Transaction>();
            var latestBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var fromBlock = new HexBigInteger(Math.Max(0, latestBlock.Value - limit));

            // Get ETH transactions
            var filter = _web3.Eth.GetNewFilterForEvent<TransferEventDTO>();
            filter.NewFilterInput = new NewFilterInput
            {
                FromBlock = BlockParameter.CreateEarliest(),
                ToBlock = BlockParameter.CreateLatest(),
                Address = new[] { address }
            };

            // Note: This is a simplified implementation
            // In production, you'd use event filters or external indexing services like The Graph
            
            return transactions.OrderByDescending(t => t.CreatedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transactions for address {Address}", address);
            return Array.Empty<Transaction>();
        }
    }

    public async Task<TransactionResult> SendTransactionAsync(TransactionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_addressValidator.IsValidEthereumAddress(request.ToAddress))
            {
                return new TransactionResult
                {
                    Success = false,
                    Error = $"Invalid destination address: {request.ToAddress}"
                };
            }

            if (request.Amount <= 0)
            {
                return new TransactionResult
                {
                    Success = false,
                    Error = "Amount must be greater than zero"
                };
            }

            if (string.IsNullOrEmpty(request.PrivateKey))
            {
                return new TransactionResult
                {
                    Success = false,
                    Error = "Private key is required"
                };
            }

            var gasEstimate = await EstimateGasForTransferAsync(
                request.FromAddress ?? string.Empty, 
                request.ToAddress, 
                Web3.Convert.ToWei(request.Amount), 
                request.TokenContract, 
                cancellationToken);

            string transactionHash;
            
            if (string.IsNullOrEmpty(request.TokenContract))
            {
                transactionHash = await SendEthAsync(
                    request.FromAddress ?? string.Empty,
                    request.ToAddress,
                    request.Amount,
                    request.PrivateKey,
                    gasEstimate,
                    cancellationToken);
            }
            else
            {
                transactionHash = await SendTokenAsync(
                    request.FromAddress ?? string.Empty,
                    request.ToAddress,
                    request.Amount,
                    request.TokenContract,
                    request.PrivateKey,
                    gasEstimate,
                    cancellationToken);
            }

            var transaction = await GetTransactionAsync(transactionHash, cancellationToken);

            return new TransactionResult
            {
                Success = true,
                TransactionHash = transactionHash,
                EstimatedFee = gasEstimate.EstimatedCost,
                Transaction = transaction
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Ethereum transaction");
            return new TransactionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<string> SendEthAsync(string from, string to, decimal amount, string privateKey, GasEstimate gasEstimate, CancellationToken cancellationToken = default)
    {
        try
        {
            var account = new Account(privateKey);
            var web3 = new Web3(account, _options.RpcEndpoint);

            var transaction = new TransactionInput
            {
                From = from,
                To = to,
                Value = new HexBigInteger(Web3.Convert.ToWei(amount)),
                Gas = new HexBigInteger(gasEstimate.GasLimit),
                MaxFeePerGas = new HexBigInteger(gasEstimate.MaxFeePerGas),
                MaxPriorityFeePerGas = new HexBigInteger(gasEstimate.MaxPriorityFeePerGas)
            };

            var txHash = await web3.Eth.TransactionManager.SendTransactionAsync(transaction);
            _logger.LogInformation("Sent {Amount} ETH from {From} to {To}, transaction: {TxHash}", amount, from, to, txHash);
            
            return txHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send ETH");
            throw new InvalidOperationException("Failed to send ETH", ex);
        }
    }

    public async Task<string> SendTokenAsync(string from, string to, decimal amount, string tokenContract, string privateKey, GasEstimate gasEstimate, CancellationToken cancellationToken = default)
    {
        try
        {
            var account = new Account(privateKey);
            var web3 = new Web3(account, _options.RpcEndpoint);
            var tokenService = new Erc20Service(web3, tokenContract);

            var decimals = await tokenService.DecimalsQueryAsync();
            var amountInSmallestUnit = Web3.Convert.ToWei(amount, decimals);

            var transferFunction = new TransferFunction
            {
                To = to,
                TokenAmount = amountInSmallestUnit,
                Gas = gasEstimate.GasLimit,
                MaxFeePerGas = gasEstimate.MaxFeePerGas,
                MaxPriorityFeePerGas = gasEstimate.MaxPriorityFeePerGas
            };

            var txHash = await tokenService.TransferRequestAndWaitForReceiptAsync(transferFunction);
            _logger.LogInformation("Sent {Amount} tokens from {From} to {To}, transaction: {TxHash}", amount, from, to, txHash.TransactionHash);
            
            return txHash.TransactionHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send token");
            throw new InvalidOperationException("Failed to send token", ex);
        }
    }

    public async Task<bool> ValidateAddressAsync(string address, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_addressValidator.IsValidEthereumAddress(address));
    }

    public async Task<BlockchainInfo> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var blockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(blockNumber);
            var gasPrice = await _web3.Eth.GasPrice.SendRequestAsync();

            return new BlockchainInfo
            {
                Network = _options.Network.ToString(),
                BlockHeight = (long)blockNumber.Value,
                BlockHash = block?.BlockHash ?? string.Empty,
                NetworkFee = Web3.Convert.FromWei(gasPrice.Value),
                IsConnected = true,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Ethereum network info");
            return new BlockchainInfo
            {
                Network = _options.Network.ToString(),
                IsConnected = false,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    public async Task<GasEstimate> EstimateGasAsync(TransactionRequest request, CancellationToken cancellationToken = default)
    {
        return await EstimateGasForTransferAsync(
            request.FromAddress ?? string.Empty,
            request.ToAddress,
            Web3.Convert.ToWei(request.Amount),
            request.TokenContract,
            cancellationToken);
    }

    public async Task<GasEstimate> EstimateGasForTransferAsync(string from, string to, BigInteger amount, string? tokenContract = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var latestBlock = await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(BlockParameter.CreateLatest());
            var baseFeePerGas = latestBlock.BaseFeePerGas?.Value ?? _options.MaxFeePerGas;

            HexBigInteger gasEstimate;
            
            if (string.IsNullOrEmpty(tokenContract))
            {
                // ETH transfer
                gasEstimate = await _web3.Eth.TransactionManager.EstimateGasAsync(new TransactionInput
                {
                    From = from,
                    To = to,
                    Value = new HexBigInteger(amount)
                });
            }
            else
            {
                // Token transfer
                var tokenService = new Erc20Service(_web3, tokenContract);
                var transferFunction = new TransferFunction { To = to, TokenAmount = amount };
                gasEstimate = await tokenService.TransferRequestAsync(transferFunction);
            }

            var maxFeePerGas = baseFeePerGas * 2 + _options.MaxPriorityFeePerGas;
            var estimatedCost = Web3.Convert.FromWei(gasEstimate.Value * maxFeePerGas);

            return new GasEstimate
            {
                GasLimit = gasEstimate.Value,
                GasPrice = baseFeePerGas,
                MaxFeePerGas = maxFeePerGas,
                MaxPriorityFeePerGas = _options.MaxPriorityFeePerGas,
                EstimatedCost = estimatedCost,
                Currency = SupportedCurrency
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to estimate gas, using defaults");
            return new GasEstimate
            {
                GasLimit = _options.GasLimit,
                GasPrice = _options.MaxFeePerGas,
                MaxFeePerGas = _options.MaxFeePerGas,
                MaxPriorityFeePerGas = _options.MaxPriorityFeePerGas,
                EstimatedCost = Web3.Convert.FromWei(_options.GasLimit * _options.MaxFeePerGas),
                Currency = SupportedCurrency
            };
        }
    }

    public async Task<bool> IsTransactionConfirmedAsync(string transactionHash, int requiredConfirmations, CancellationToken cancellationToken = default)
    {
        var confirmations = await GetConfirmationsAsync(transactionHash, cancellationToken);
        return confirmations >= requiredConfirmations;
    }

    public async Task<string> GetLatestBlockAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var blockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            return blockNumber.Value.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest block");
            throw new InvalidOperationException("Failed to get latest block", ex);
        }
    }

    public async Task<bool> IsValidTokenContractAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_addressValidator.IsValidEthereumAddress(contractAddress))
                return false;

            var tokenService = new Erc20Service(_web3, contractAddress);
            await tokenService.NameQueryAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> GetConfirmationsAsync(string transactionHash, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash);
            if (receipt?.BlockNumber == null) return 0;

            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            return (int)(currentBlock.Value - receipt.BlockNumber.Value) + 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get confirmations for transaction {Hash}", transactionHash);
            return 0;
        }
    }

    private static decimal CalculateTransactionFee(Nethereum.RPC.Eth.DTOs.Transaction transaction, TransactionReceipt? receipt)
    {
        if (receipt?.GasUsed == null) return 0;

        var gasUsed = receipt.GasUsed.Value;
        var gasPrice = transaction.GasPrice?.Value ?? 0;
        
        return Web3.Convert.FromWei(gasUsed * gasPrice);
    }

    private static TransactionStatus GetTransactionStatus(int confirmations, bool isSuccessful)
    {
        if (confirmations == 0) return TransactionStatus.Pending;
        if (!isSuccessful) return TransactionStatus.Failed;
        return confirmations >= 12 ? TransactionStatus.Confirmed : TransactionStatus.Confirming;
    }

    private string GetTokenSymbol(string tokenContract)
    {
        return _options.TokenContracts.FirstOrDefault(t => t.Value.Equals(tokenContract, StringComparison.OrdinalIgnoreCase)).Key ?? "TOKEN";
    }
}