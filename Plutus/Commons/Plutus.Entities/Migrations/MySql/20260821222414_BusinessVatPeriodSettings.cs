using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class BusinessVatPeriodSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinancialYearStartDay",
                table: "Business",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinancialYearStartMonth",
                table: "Business",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatBasis",
                table: "Business",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "VatSettingsChangedAtUtc",
                table: "Business",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VatSettingsChangedBy",
                table: "Business",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "VatStaggerEndMonth",
                table: "Business",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinancialYearStartDay",
                table: "Business");

            migrationBuilder.DropColumn(
                name: "FinancialYearStartMonth",
                table: "Business");

            migrationBuilder.DropColumn(
                name: "VatBasis",
                table: "Business");

            migrationBuilder.DropColumn(
                name: "VatSettingsChangedAtUtc",
                table: "Business");

            migrationBuilder.DropColumn(
                name: "VatSettingsChangedBy",
                table: "Business");

            migrationBuilder.DropColumn(
                name: "VatStaggerEndMonth",
                table: "Business");
        }
    }
}
