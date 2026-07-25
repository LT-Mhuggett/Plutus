using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddCashAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TillId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeviceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AmountPence = table.Column<long>(type: "bigint", nullable: false),
                    CountedPence = table.Column<long>(type: "bigint", nullable: true),
                    ExpectedPence = table.Column<long>(type: "bigint", nullable: true),
                    VariancePence = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OperatorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderRef = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AmountPence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MatchedSaleId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CashEvents_TenantId",
                table: "CashEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CashEvents_TenantId_TillId_BusinessDay",
                table: "CashEvents",
                columns: new[] { "TenantId", "TillId", "BusinessDay" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_TenantId",
                table: "PaymentEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_TenantId_ProviderRef",
                table: "PaymentEvents",
                columns: new[] { "TenantId", "ProviderRef" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_TenantId_ResolvedAtUtc",
                table: "PaymentEvents",
                columns: new[] { "TenantId", "ResolvedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashEvents");

            migrationBuilder.DropTable(
                name: "PaymentEvents");
        }
    }
}
