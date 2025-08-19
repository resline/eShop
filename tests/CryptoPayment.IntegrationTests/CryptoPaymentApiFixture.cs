using Aspire.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace CryptoPayment.IntegrationTests;

public class CryptoPaymentApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly RabbitMqContainer _rabbitMqContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _httpClient;

    public CryptoPaymentApiFixture()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithDatabase("cryptopayment_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .WithCleanUp(true)
            .Build();

        _rabbitMqContainer = new RabbitMqBuilder()
            .WithUsername("test")
            .WithPassword("test")
            .WithCleanUp(true)
            .Build();
    }

    public HttpClient HttpClient => _httpClient ?? throw new InvalidOperationException("Fixture not initialized");
    public string ConnectionString => _postgresContainer.GetConnectionString();
    public string RabbitMqConnectionString => _rabbitMqContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                        ["ConnectionStrings:EventBus"] = RabbitMqConnectionString,
                        ["EventBus:SubscriptionClientName"] = "CryptoPayment.IntegrationTests"
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Remove the existing DbContext registration
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<CryptoPaymentContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add new DbContext with test database
                    services.AddDbContext<CryptoPaymentContext>(options =>
                        options.UseNpgsql(ConnectionString));

                    // Ensure database is created and seeded
                    var serviceProvider = services.BuildServiceProvider();
                    using var scope = serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<CryptoPaymentContext>();
                    context.Database.EnsureCreated();
                    
                    // Seed test data
                    SeedTestData(context);
                });
            });

        _httpClient = _factory.CreateClient();
    }

    private static void SeedTestData(CryptoPaymentContext context)
    {
        if (!context.CryptoCurrencies.Any())
        {
            context.CryptoCurrencies.AddRange(
                new CryptoCurrency
                {
                    Id = 1,
                    Symbol = "BTC",
                    Name = "Bitcoin",
                    Decimals = 8,
                    NetworkName = "Bitcoin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new CryptoCurrency
                {
                    Id = 2,
                    Symbol = "ETH",
                    Name = "Ethereum",
                    Decimals = 18,
                    NetworkName = "Ethereum",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
        }

        if (!context.PaymentAddresses.Any())
        {
            context.PaymentAddresses.AddRange(
                new PaymentAddress
                {
                    Address = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
                    CryptoCurrencyId = 1,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                },
                new PaymentAddress
                {
                    Address = "0x742d35Cc6635C0532925a3b8D7386DdA9f2777c4",
                    CryptoCurrencyId = 2,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                }
            );
        }

        context.SaveChanges();
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        _factory?.Dispose();
        await _postgresContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();
        await _postgresContainer.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();
    }

    public async Task<T> GetRequiredService<T>() where T : notnull
    {
        if (_factory == null)
            throw new InvalidOperationException("Fixture not initialized");

        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    public async Task ExecuteDbContextAsync(Func<CryptoPaymentContext, Task> action)
    {
        if (_factory == null)
            throw new InvalidOperationException("Fixture not initialized");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CryptoPaymentContext>();
        await action(context);
    }
}