using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddVatBandIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VatRollups_TenantId_StoreId_BusinessDay_VatRateBp",
                table: "VatRollups");

            migrationBuilder.AddColumn<string>(
                name: "VatBand",
                table: "VatRollups",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VatBand",
                table: "SaleLines",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VatBandTaxMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LegacyTaxId = table.Column<int>(type: "int", nullable: false),
                    Band = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VatBandTaxMaps", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VatRollups_TenantId_StoreId_BusinessDay_VatRateBp_VatBand",
                table: "VatRollups",
                columns: new[] { "TenantId", "StoreId", "BusinessDay", "VatRateBp", "VatBand" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VatBandTaxMaps_TenantId",
                table: "VatBandTaxMaps",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VatBandTaxMaps_TenantId_LegacyTaxId",
                table: "VatBandTaxMaps",
                columns: new[] { "TenantId", "LegacyTaxId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VatBandTaxMaps");

            migrationBuilder.DropIndex(
                name: "IX_VatRollups_TenantId_StoreId_BusinessDay_VatRateBp_VatBand",
                table: "VatRollups");

            migrationBuilder.DropColumn(
                name: "VatBand",
                table: "VatRollups");

            migrationBuilder.DropColumn(
                name: "VatBand",
                table: "SaleLines");

            migrationBuilder.CreateIndex(
                name: "IX_VatRollups_TenantId_StoreId_BusinessDay_VatRateBp",
                table: "VatRollups",
                columns: new[] { "TenantId", "StoreId", "BusinessDay", "VatRateBp" },
                unique: true);
        }
    }
}
