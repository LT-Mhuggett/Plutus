using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformFlags",
                columns: table => new
                {
                    FlagName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFlags", x => x.FlagName);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TenantEntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Entitlement = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Deny = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEntitlementOverrides", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlementOverrides_TenantId",
                table: "TenantEntitlementOverrides",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformFlags");

            migrationBuilder.DropTable(
                name: "TenantEntitlementOverrides");
        }
    }
}
