using NBitcoin;
using NBitcoin.RPC;
using RestSharp;
using CryptoPayment.BlockchainServices.Abstractions;
using CryptoPayment.BlockchainServices.Models;
using CryptoPayment.BlockchainServices.Configuration;

namespace CryptoPayment.BlockchainServices.Services;

public class BitcoinService : IBitcoinService
{
    private readonly ILogger<BitcoinService> _logger;
    private readonly BitcoinOptions _options;
    private readonly IAddressValidator _addressValidator;
    private readonly IKeyManager _keyManager;
    private readonly RPCClient _rpcClient;
    private readonly Network _network;
    private readonly ExtKey _masterKey;

    public string SupportedCurrency => "BTC";
    public bool SupportsTokens => false;

    public BitcoinService(
        ILogger<BitcoinService> logger,
        IOptions<BlockchainOptions> options,
        IAddressValidator addressValidator,
        IKeyManager keyManager)
    {
        _logger = logger;
        _options = options.Value.Bitcoin;
        _addressValidator = addressValidator;
        _keyManager = keyManager;
        
        _network = _options.Network == NetworkType.Mainnet ? Network.Main : Network.TestNet;
        
        var rpcCredentials = new RPCCredentialString
        {
            UserPassword = new NetworkCredential(_options.RpcUsername, _options.RpcPassword)
        };
        
        _rpcClient = new RPCClient(rpcCredentials, _options.RpcEndpoint, _network);
        
        // Generate or load master key for HD wallet
        _masterKey = new ExtKey();
        
        _logger.LogInformation("Bitcoin service initialized for {Network}", _network.Name);
    }

    public async Task<PaymentAddress> GenerateAddressAsync(string? label = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate HD wallet address
            var derivationPath = new KeyPath($"m/44'/0'/0'/0/{await GetNextDerivationIndexAsync(cancellationToken)}");
            var childKey = _masterKey.Derive(derivationPath);
            
            var address = childKey.PrivateKey.GetWitAddress(_network).ToString();
            var privateKey = childKey.PrivateKey.GetWif(_network).ToString();
            var publicKey = childKey.PrivateKey.PubKey.ToString();

            var paymentAddress = new PaymentAddress
            {
                Address = address,
                Currency = SupportedCurrency,
                PrivateKey = await _keyManager.EncryptPrivateKeyAsync(privateKey, cancellationToken),
                PublicKey = publicKey,
                DerivationIndex = derivationPath.Indexes.Last(),
                Label = label,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Generated Bitcoin address {Address} with label {Label}", address, label);
            return paymentAddress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Bitcoin address");
            throw new InvalidOperationException("Failed to generate Bitcoin address", ex);
        }
    }

    public async Task<string> GetNewAddressAsync(string? label = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var address = await _rpcClient.GetNewAddressAsync(label);
            _logger.LogInformation("Generated new Bitcoin address {Address}", address);
            return address.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get new Bitcoin address via RPC");
            throw new InvalidOperationException("Failed to get new Bitcoin address", ex);
        }
    }

    public async Task<AddressBalance> GetBalanceAsync(string address, string? tokenContract = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_addressValidator.IsValidBitcoinAddress(address, _options.Network))
            {
                throw new ArgumentException($"Invalid Bitcoin address: {address}");
            }

            var balance = await _rpcClient.GetReceivedByAddressAsync(BitcoinAddress.Create(address, _network));
            var unconfirmedBalance = await GetUnconfirmedBalanceAsync(address, cancellationToken);

            return new AddressBalance
            {
                Address = address,
                Balance = (decimal)balance.ToDecimal(MoneyUnit.BTC),
                Currency = SupportedCurrency,
                ConfirmedBalance = (decimal)balance.ToDecimal(MoneyUnit.BTC),
                UnconfirmedBalance = unconfirmedBalance,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get balance for address {Address}", address);
            throw new InvalidOperationException($"Failed to get balance for address {address}", ex);
        }
    }

    public async Task<Transaction?> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var txId = new uint256(transactionHash);
            var rpcTx = await _rpcClient.GetRawTransactionAsync(txId, true);
            var blockInfo = rpcTx.BlockHash != null ? await _rpcClient.GetBlockAsync(rpcTx.BlockHash) : null;

            var confirmations = await GetConfirmationsAsync(transactionHash, cancellationToken);
            var status = GetTransactionStatus(confirmations, _options.ConfirmationsRequired);

            return new Transaction
            {
                Id = transactionHash,
                Hash = transactionHash,
                FromAddress = ExtractFromAddress(rpcTx),
                ToAddress = ExtractToAddress(rpcTx),
                Amount = ExtractAmount(rpcTx),
                Currency = SupportedCurrency,
                Status = status,
                Confirmations = confirmations,
                RequiredConfirmations = _options.ConfirmationsRequired,
                Fee = (decimal)(rpcTx.Transaction.GetFee()?.ToDecimal(MoneyUnit.BTC) ?? 0),
                CreatedAt = blockInfo?.Header.BlockTime.DateTime ?? DateTime.UtcNow,
                ConfirmedAt = status == TransactionStatus.Confirmed ? blockInfo?.Header.BlockTime.DateTime : null,
                BlockHash = rpcTx.BlockHash?.ToString(),
                BlockNumber = blockInfo?.Height
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
            var listTransactions = await _rpcClient.ListTransactionsAsync("*", limit);

            foreach (var tx in listTransactions.Where(t => t.Address?.ToString() == address))
            {
                var transaction = await GetTransactionAsync(tx.TransactionId.ToString(), cancellationToken);
                if (transaction != null)
                {
                    transactions.Add(transaction);
                }
            }

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
            if (!_addressValidator.IsValidBitcoinAddress(request.ToAddress, _options.Network))
            {
                return new TransactionResult
                {
                    Success = false,
                    Error = $"Invalid destination address: {request.ToAddress}"
                };
            }

            if (request.Amount < _options.MinimumAmount)
            {
                return new TransactionResult
                {
                    Success = false,
                    Error = $"Amount {request.Amount} is below minimum {_options.MinimumAmount}"
                };
            }

            var fee = await EstimateFeeAsync(6, cancellationToken);
            var transactionHash = await SendToAddressAsync(request.ToAddress, request.Amount, request.PrivateKey, cancellationToken);

            var transaction = await GetTransactionAsync(transactionHash, cancellationToken);

            return new TransactionResult
            {
                Success = true,
                TransactionHash = transactionHash,
                EstimatedFee = fee,
                Transaction = transaction
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Bitcoin transaction");
            return new TransactionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<string> SendToAddressAsync(string address, decimal amount, string? privateKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var bitcoinAddress = BitcoinAddress.Create(address, _network);
            var money = Money.FromUnit(amount, MoneyUnit.BTC);
            
            var txId = await _rpcClient.SendToAddressAsync(bitcoinAddress, money);
            _logger.LogInformation("Sent {Amount} BTC to {Address}, transaction: {TxId}", amount, address, txId);
            
            return txId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Bitcoin to address {Address}", address);
            throw new InvalidOperationException($"Failed to send Bitcoin to address {address}", ex);
        }
    }

    public async Task<bool> ValidateAddressAsync(string address, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_addressValidator.IsValidBitcoinAddress(address, _options.Network));
    }

    public async Task<BlockchainInfo> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var blockchainInfo = await _rpcClient.GetBlockchainInfoAsync();
            var bestBlockHash = await _rpcClient.GetBestBlockHashAsync();
            var block = await _rpcClient.GetBlockAsync(bestBlockHash);

            return new BlockchainInfo
            {
                Network = _network.Name,
                BlockHeight = blockchainInfo.Blocks,
                BlockHash = bestBlockHash.ToString(),
                NetworkFee = await EstimateFeeAsync(6, cancellationToken),
                IsConnected = true,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Bitcoin network info");
            return new BlockchainInfo
            {
                Network = _network.Name,
                IsConnected = false,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    public async Task<decimal> EstimateFeeAsync(int targetBlocks = 6, CancellationToken cancellationToken = default)
    {
        try
        {
            var feeRate = await _rpcClient.EstimateSmartFeeAsync(targetBlocks);
            return (decimal)(feeRate.FeeRate?.ToDecimal(MoneyUnit.BTC) ?? 0.0001m);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to estimate fee, using default");
            return 0.0001m; // Default fee
        }
    }

    public async Task<int> GetConfirmationsAsync(string transactionHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var txId = new uint256(transactionHash);
            var transaction = await _rpcClient.GetTransactionAsync(txId);
            return transaction.Confirmations ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get confirmations for transaction {Hash}", transactionHash);
            return 0;
        }
    }

    public async Task<bool> IsTransactionConfirmedAsync(string transactionHash, int requiredConfirmations, CancellationToken cancellationToken = default)
    {
        var confirmations = await GetConfirmationsAsync(transactionHash, cancellationToken);
        return confirmations >= requiredConfirmations;
    }

    public Task<GasEstimate> EstimateGasAsync(TransactionRequest request, CancellationToken cancellationToken = default)
    {
        // Bitcoin doesn't use gas, return fee estimate instead
        var gasEstimate = new GasEstimate
        {
            EstimatedCost = EstimateFeeAsync(6, cancellationToken).Result,
            Currency = SupportedCurrency
        };
        
        return Task.FromResult(gasEstimate);
    }

    private async Task<int> GetNextDerivationIndexAsync(CancellationToken cancellationToken)
    {
        // In a real implementation, this would be stored in a database
        // For now, return a random index for demonstration
        return Random.Shared.Next(0, 1000);
    }

    private async Task<decimal> GetUnconfirmedBalanceAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            var unconfirmed = await _rpcClient.GetUnconfirmedBalanceAsync();
            return (decimal)unconfirmed.ToDecimal(MoneyUnit.BTC);
        }
        catch
        {
            return 0;
        }
    }

    private static string ExtractFromAddress(RPCResponse response)
    {
        // Bitcoin transactions can have multiple inputs, return first one
        return response.Transaction?.Inputs?.FirstOrDefault()?.PrevOut?.ToString() ?? string.Empty;
    }

    private static string ExtractToAddress(RPCResponse response)
    {
        // Bitcoin transactions can have multiple outputs, return first one
        return response.Transaction?.Outputs?.FirstOrDefault()?.ScriptPubKey?.GetDestinationAddress(Network.TestNet)?.ToString() ?? string.Empty;
    }

    private static decimal ExtractAmount(RPCResponse response)
    {
        // Sum all output amounts
        return (decimal)(response.Transaction?.Outputs?.Sum(o => o.Value.ToDecimal(MoneyUnit.BTC)) ?? 0);
    }

    private static TransactionStatus GetTransactionStatus(int confirmations, int requiredConfirmations)
    {
        return confirmations switch
        {
            0 => TransactionStatus.Mempool,
            _ when confirmations < requiredConfirmations => TransactionStatus.Confirming,
            _ => TransactionStatus.Confirmed
        };
    }
}