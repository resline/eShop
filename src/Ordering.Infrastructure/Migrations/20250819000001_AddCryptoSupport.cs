using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace eShop.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCryptoSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add crypto payment fields to orders table
            migrationBuilder.AddColumn<string>(
                name: "CryptoPaymentId",
                schema: "ordering",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CryptoTransactionHash",
                schema: "ordering",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CryptoAmount",
                schema: "ordering",
                table: "orders",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CryptoPaymentInitiatedAt",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CryptoPaymentConfirmedAt",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            // Create buyer crypto wallets table
            migrationBuilder.CreateTable(
                name: "buyer_crypto_wallets",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NetworkId = table.Column<int>(type: "integer", nullable: false),
                    NetworkName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChainId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NativeCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsTestnet = table.Column<bool>(type: "boolean", nullable: false),
                    BuyerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buyer_crypto_wallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buyer_crypto_wallets_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalSchema: "ordering",
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_orders_CryptoPaymentId",
                schema: "ordering",
                table: "orders",
                column: "CryptoPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_CryptoTransactionHash",
                schema: "ordering",
                table: "orders",
                column: "CryptoTransactionHash");

            migrationBuilder.CreateIndex(
                name: "IX_buyer_crypto_wallets_Address",
                schema: "ordering",
                table: "buyer_crypto_wallets",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "IX_buyer_crypto_wallets_BuyerId",
                schema: "ordering",
                table: "buyer_crypto_wallets",
                column: "BuyerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes first
            migrationBuilder.DropIndex(
                name: "IX_orders_CryptoPaymentId",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_CryptoTransactionHash",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropTable(
                name: "buyer_crypto_wallets",
                schema: "ordering");

            // Remove crypto payment columns from orders
            migrationBuilder.DropColumn(
                name: "CryptoPaymentId",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CryptoTransactionHash",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CryptoAmount",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CryptoPaymentInitiatedAt",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CryptoPaymentConfirmedAt",
                schema: "ordering",
                table: "orders");
        }
    }
}