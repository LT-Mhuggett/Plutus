using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddConnectorRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectorRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Connector = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LastPollAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastWebhookAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastOutboundAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorStreak = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastErrorAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorRuns", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorRuns_Connector_TenantId",
                table: "ConnectorRuns",
                columns: new[] { "Connector", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorRuns");
        }
    }
}
