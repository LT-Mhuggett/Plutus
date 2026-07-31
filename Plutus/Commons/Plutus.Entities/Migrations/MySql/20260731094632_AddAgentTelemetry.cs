using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddAgentTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentPrinterName",
                table: "Devices",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "AgentPrinterOnline",
                table: "Devices",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentReportedAtUtc",
                table: "Devices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentVersion",
                table: "Devices",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentPrinterName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AgentPrinterOnline",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AgentReportedAtUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AgentVersion",
                table: "Devices");
        }
    }
}
