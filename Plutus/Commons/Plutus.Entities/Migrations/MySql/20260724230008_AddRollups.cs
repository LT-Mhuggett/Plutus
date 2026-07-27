using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesRollups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    TillId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    GrossPence = table.Column<long>(type: "bigint", nullable: false),
                    VatPence = table.Column<long>(type: "bigint", nullable: false),
                    TxnCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesRollups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VatRollups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    VatRateBp = table.Column<int>(type: "int", nullable: false),
                    GrossPence = table.Column<long>(type: "bigint", nullable: false),
                    NetPence = table.Column<long>(type: "bigint", nullable: false),
                    VatPence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VatRollups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SalesRollups_TenantId",
                table: "SalesRollups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesRollups_TenantId_CompanyId_BusinessDay",
                table: "SalesRollups",
                columns: new[] { "TenantId", "CompanyId", "BusinessDay" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesRollups_TenantId_StoreId_BusinessDay",
                table: "SalesRollups",
                columns: new[] { "TenantId", "StoreId", "BusinessDay" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesRollups_TenantId_TillId_BusinessDay",
                table: "SalesRollups",
                columns: new[] { "TenantId", "TillId", "BusinessDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VatRollups_TenantId",
                table: "VatRollups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VatRollups_TenantId_StoreId_BusinessDay_VatRateBp",
                table: "VatRollups",
                columns: new[] { "TenantId", "StoreId", "BusinessDay", "VatRateBp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesRollups");

            migrationBuilder.DropTable(
                name: "VatRollups");
        }
    }
}
