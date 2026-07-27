using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddSalesV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsumerOffsets",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastOutboxId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerOffsets", x => x.ConsumerName);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EventType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SaleAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    OriginalSaleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdjustmentSaleId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ItemId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Qty = table.Column<int>(type: "int", nullable: true),
                    AmountPence = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthoriserUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleAdjustments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SaleQuarantine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleQuarantine", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SalesV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TillId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeviceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeviceSeq = table.Column<long>(type: "bigint", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GrossPence = table.Column<long>(type: "bigint", nullable: false),
                    VatPence = table.Column<long>(type: "bigint", nullable: false),
                    LegacyRef = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OperatorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VatReconstructed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesV2", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SaleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitPricePence = table.Column<long>(type: "bigint", nullable: false),
                    LineGrossPence = table.Column<long>(type: "bigint", nullable: false),
                    DiscountPence = table.Column<long>(type: "bigint", nullable: false),
                    VatRateBp = table.Column<int>(type: "int", nullable: false),
                    VatAmountPence = table.Column<long>(type: "bigint", nullable: false),
                    OverriddenFromPence = table.Column<long>(type: "bigint", nullable: true),
                    DiscountsJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLines_SalesV2_SaleId",
                        column: x => x.SaleId,
                        principalTable: "SalesV2",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SaleTenders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SaleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenderType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AmountPence = table.Column<long>(type: "bigint", nullable: false),
                    ChangePence = table.Column<long>(type: "bigint", nullable: false),
                    ProviderRef = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleTenders_SalesV2_SaleId",
                        column: x => x.SaleId,
                        principalTable: "SalesV2",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_EventId",
                table: "OutboxEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleAdjustments_TenantId",
                table: "SaleAdjustments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleAdjustments_TenantId_OriginalSaleId",
                table: "SaleAdjustments",
                columns: new[] { "TenantId", "OriginalSaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_TenantId",
                table: "SaleLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_TenantId_ItemId",
                table: "SaleLines",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_TenantId_SaleId",
                table: "SaleLines",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesV2_TenantId",
                table: "SalesV2",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesV2_TenantId_BusinessDay",
                table: "SalesV2",
                columns: new[] { "TenantId", "BusinessDay" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesV2_TenantId_DeviceId_DeviceSeq",
                table: "SalesV2",
                columns: new[] { "TenantId", "DeviceId", "DeviceSeq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesV2_TenantId_TillId_BusinessDay",
                table: "SalesV2",
                columns: new[] { "TenantId", "TillId", "BusinessDay" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleTenders_SaleId",
                table: "SaleTenders",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleTenders_TenantId",
                table: "SaleTenders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleTenders_TenantId_SaleId",
                table: "SaleTenders",
                columns: new[] { "TenantId", "SaleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumerOffsets");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "SaleAdjustments");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "SaleQuarantine");

            migrationBuilder.DropTable(
                name: "SaleTenders");

            migrationBuilder.DropTable(
                name: "SalesV2");
        }
    }
}
