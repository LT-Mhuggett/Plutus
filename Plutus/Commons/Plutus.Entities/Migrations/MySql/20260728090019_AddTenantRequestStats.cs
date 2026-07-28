using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddTenantRequestStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantRequestStats",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MinuteUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RouteGroup = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    Err4xx = table.Column<long>(type: "bigint", nullable: false),
                    Err5xx = table.Column<long>(type: "bigint", nullable: false),
                    P50Ms = table.Column<int>(type: "int", nullable: false),
                    P95Ms = table.Column<int>(type: "int", nullable: false),
                    MaxMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRequestStats", x => new { x.TenantId, x.MinuteUtc, x.RouteGroup });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRequestStats_MinuteUtc",
                table: "TenantRequestStats",
                column: "MinuteUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRequestStats_TenantId",
                table: "TenantRequestStats",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantRequestStats");
        }
    }
}
