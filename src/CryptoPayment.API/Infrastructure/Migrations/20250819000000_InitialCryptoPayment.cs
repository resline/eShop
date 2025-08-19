using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace eShop.CryptoPayment.API.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCryptoPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CryptoCurrencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Decimals = table.Column<int>(type: "integer", nullable: false),
                    NetworkName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ContractAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CryptoCurrencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationEventLogs",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventTypeName = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    TimesSent = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TransactionId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEventLogs", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CryptoCurrencyId = table.Column<int>(type: "integer", nullable: false),
                    PrivateKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAddresses_CryptoCurrencies_CryptoCurrencyId",
                        column: x => x.CryptoCurrencyId,
                        principalTable: "CryptoCurrencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CryptoPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CryptoCurrencyId = table.Column<int>(type: "integer", nullable: false),
                    PaymentAddressId = table.Column<int>(type: "integer", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ReceivedAmount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BlockNumber = table.Column<int>(type: "integer", nullable: true),
                    Confirmations = table.Column<int>(type: "integer", nullable: true),
                    RequiredConfirmations = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BuyerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CryptoPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CryptoPayments_CryptoCurrencies_CryptoCurrencyId",
                        column: x => x.CryptoCurrencyId,
                        principalTable: "CryptoCurrencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CryptoPayments_PaymentAddresses_PaymentAddressId",
                        column: x => x.PaymentAddressId,
                        principalTable: "PaymentAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CryptoCurrencies",
                columns: new[] { "Id", "ContractAddress", "CreatedAt", "Decimals", "IsActive", "Name", "NetworkName", "Symbol", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, null, new DateTime(2025, 8, 19, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, "Bitcoin", "Bitcoin", "BTC", new DateTime(2025, 8, 19, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, null, new DateTime(2025, 8, 19, 0, 0, 0, 0, DateTimeKind.Utc), 18, true, "Ethereum", "Ethereum", "ETH", new DateTime(2025, 8, 19, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_BuyerId",
                table: "CryptoPayments",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_CreatedAt",
                table: "CryptoPayments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_CryptoCurrencyId",
                table: "CryptoPayments",
                column: "CryptoCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_PaymentAddressId",
                table: "CryptoPayments",
                column: "PaymentAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_PaymentId",
                table: "CryptoPayments",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_Status",
                table: "CryptoPayments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoPayments_TransactionHash",
                table: "CryptoPayments",
                column: "TransactionHash");

            migrationBuilder.CreateIndex(
                name: "IX_CryptoCurrencies_Symbol",
                table: "CryptoCurrencies",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAddresses_Address",
                table: "PaymentAddresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAddresses_CreatedAt",
                table: "PaymentAddresses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAddresses_CryptoCurrencyId",
                table: "PaymentAddresses",
                column: "CryptoCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAddresses_CryptoCurrencyId_IsUsed",
                table: "PaymentAddresses",
                columns: new[] { "CryptoCurrencyId", "IsUsed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CryptoPayments");

            migrationBuilder.DropTable(
                name: "IntegrationEventLogs");

            migrationBuilder.DropTable(
                name: "PaymentAddresses");

            migrationBuilder.DropTable(
                name: "CryptoCurrencies");
        }
    }
}