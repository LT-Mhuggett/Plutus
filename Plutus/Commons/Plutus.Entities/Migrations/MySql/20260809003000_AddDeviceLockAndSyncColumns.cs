using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Plutus.Entities;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <summary>
    /// ⚠ A CATCH-UP MIGRATION, written by hand on 2026-08-09 after this exact gap took the test
    /// backend down for ~10 minutes.
    ///
    /// WHAT HAPPENED. `Device.SyncNow`, `Device.Locked` and `Device.LockReason` were added to the
    /// entity for WP5, and they reached `MySqlDbContextModelSnapshot` — but the migration generated
    /// alongside them (`20260808142202_AddDeviceSyncSignals`) contains ONLY the
    /// `IX_Items_Tenant_Modified_IdOne` index. Its name says otherwise, which is what made it
    /// invisible: the snapshot said the columns existed, so `migrations add` had nothing left to
    /// emit, and every review of "did the migration land?" looked at the history table and saw the
    /// right name at the head.
    ///
    /// THE SYMPTOM. EF builds its SELECT from the model, so every query that materialised a Device
    /// asked MySQL for columns that were never created:
    ///   MySqlException: Unknown column 'd.LockReason' in 'field list'
    /// which 500s `POST /api/v1/tokens/device` — so **no till on the estate could obtain a token**,
    /// not merely the new sync endpoints. `/api/v1/ping` kept answering 200 throughout because it
    /// touches no database, which is precisely why a health check is not a deploy verification.
    ///
    /// ⚠ THE LESSON, for whoever writes the next one: a green `__EFMigrationsHistory` proves a
    /// migration RAN, never that it did what its name claims. Check the columns.
    ///
    /// Types are taken from the snapshot so the schema matches the model exactly — a
    /// `varchar(255)` here would drift the two apart again, silently, in the other direction.
    /// </summary>
    [Migration("20260809003000_AddDeviceLockAndSyncColumns")]
    [DbContext(typeof(MySqlDbContext))]
    public partial class AddDeviceLockAndSyncColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SyncNow",
                table: "Devices",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Locked",
                table: "Devices",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockReason",
                table: "Devices",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LockReason", table: "Devices");
            migrationBuilder.DropColumn(name: "Locked", table: "Devices");
            migrationBuilder.DropColumn(name: "SyncNow", table: "Devices");
        }
    }
}
