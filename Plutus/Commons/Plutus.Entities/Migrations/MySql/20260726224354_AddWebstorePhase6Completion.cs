using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddWebstorePhase6Completion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastFullProductSweepUtc",
                table: "WebStores",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductsCursorUtc",
                table: "WebStores",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebstoreNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WebStoreId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StoreId = table.Column<int>(type: "int", nullable: true),
                    WooOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AckedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AckedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebstoreNotifications", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WebstoreProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WebStoreId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WooProductId = table.Column<long>(type: "bigint", nullable: false),
                    Sku = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PricePence = table.Column<long>(type: "bigint", nullable: false),
                    RegularPricePence = table.Column<long>(type: "bigint", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: true),
                    StockStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Permalink = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WooModifiedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebstoreProducts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WebstoreNotifications_TenantId",
                table: "WebstoreNotifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WebstoreNotifications_TenantId_AckedAtUtc",
                table: "WebstoreNotifications",
                columns: new[] { "TenantId", "AckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebstoreNotifications_TenantId_WebStoreId_WooOrderId",
                table: "WebstoreNotifications",
                columns: new[] { "TenantId", "WebStoreId", "WooOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebstoreProducts_TenantId",
                table: "WebstoreProducts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WebstoreProducts_TenantId_WebStoreId_Sku",
                table: "WebstoreProducts",
                columns: new[] { "TenantId", "WebStoreId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_WebstoreProducts_TenantId_WebStoreId_WooProductId",
                table: "WebstoreProducts",
                columns: new[] { "TenantId", "WebStoreId", "WooProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebstoreNotifications");

            migrationBuilder.DropTable(
                name: "WebstoreProducts");

            migrationBuilder.DropColumn(
                name: "LastFullProductSweepUtc",
                table: "WebStores");

            migrationBuilder.DropColumn(
                name: "ProductsCursorUtc",
                table: "WebStores");
        }
    }
}
