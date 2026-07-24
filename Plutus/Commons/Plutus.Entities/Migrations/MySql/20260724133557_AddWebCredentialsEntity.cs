using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddWebCredentialsEntity : Migration
    {
        // WebCredentials already exists on every real DB (login has always used it). This
        // migration only brings the EF model in sync; the create is guarded so applying to the
        // existing plutus/plutus_t1 is a no-op, while a fresh DB still gets the table. Down does
        // NOT drop it — it is shared, pre-existing data.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `WebCredentials` (
                    `Email` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
                    `EmployeeId` char(36) COLLATE ascii_general_ci NOT NULL,
                    `HashedPassword` longtext CHARACTER SET utf8mb4 NOT NULL,
                    `Salt` longtext CHARACTER SET utf8mb4 NOT NULL,
                    CONSTRAINT `PK_WebCredentials` PRIMARY KEY (`Email`)
                ) CHARACTER SET=utf8mb4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: never drop the shared, pre-existing WebCredentials table.
        }
    }
}
