using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class SupportTicketReadAndClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClientLastReadAtUtc",
                table: "SupportTickets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "SupportTickets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosureRequestedAtUtc",
                table: "SupportTickets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClosureRequestedByOperator",
                table: "SupportTickets",
                type: "tinyint(1)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientLastReadAtUtc",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ClosureRequestedAtUtc",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ClosureRequestedByOperator",
                table: "SupportTickets");
        }
    }
}
