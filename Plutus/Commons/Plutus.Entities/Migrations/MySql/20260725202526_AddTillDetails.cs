using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddTillDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TillDetails",
                columns: table => new
                {
                    TillId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillDetails", x => x.TillId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TillDetails_TenantId",
                table: "TillDetails",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TillDetails_TenantId_Name",
                table: "TillDetails",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            // WP11.1 backfill: existing tills predate the name column, so give each a default
            // "Till {first-8-of-id}" (matches the API's display fallback) that operators can rename.
            // Unique per tenant by construction (the id prefix), so the unique index is satisfied.
            migrationBuilder.Sql(@"
                INSERT INTO TillDetails (TillId, TenantId, Name)
                SELECT t.Id, t.TenantId, CONCAT('Till ', SUBSTRING(t.Id, 1, 8))
                FROM Till t
                LEFT JOIN TillDetails d ON d.TillId = t.Id
                WHERE d.TillId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TillDetails");
        }
    }
}
