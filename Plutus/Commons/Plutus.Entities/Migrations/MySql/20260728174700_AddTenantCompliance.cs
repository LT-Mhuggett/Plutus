using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddTenantCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataRegion",
                table: "Tenants",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "UK")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DpaRef",
                table: "Tenants",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DpaSignedAtUtc",
                table: "Tenants",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataRegion",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DpaRef",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DpaSignedAtUtc",
                table: "Tenants");
        }
    }
}
