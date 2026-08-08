using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddDeviceSyncSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BinnedAtUtc",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StockUntracked",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAtUtc",
                table: "Employees",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BinnedAtUtc",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StockUntracked",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LastLoginAtUtc",
                table: "Employees");
        }
    }
}
