using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddQuarantineSaleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "SaleQuarantine",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_SaleQuarantine_TenantId_SaleId",
                table: "SaleQuarantine",
                columns: new[] { "TenantId", "SaleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaleQuarantine_TenantId_SaleId",
                table: "SaleQuarantine");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "SaleQuarantine");
        }
    }
}
