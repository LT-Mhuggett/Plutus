using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddDiscountSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠⚠ HAND-EDITED: EF GENERATED `defaultValue: false` HERE AND THAT WOULD HAVE BEEN WRONG.
            // A C# property initialiser (`public bool Active { get; set; } = true;`) is applied by the
            // CLR when EF materialises a NEW object — it is not a schema default and it does not touch
            // rows that already exist. So the generated version would have written `Active = 0` onto
            // every discount a shop already had, and the v1 rules feed (which filters on it) would have
            // reported that the shop had no discounts at all.
            //
            // ⚠ `defaultValue: true` fixes both halves: MySQL backfills the existing rows with 1, AND
            // the column carries `DEFAULT TRUE`, so an INSERT through the legacy `/api/Discount` CRUD —
            // which knows nothing about this column — still produces a live rule rather than a dead one.
            //
            // ⚠ Deliberately NOT declared as `HasDefaultValue` in the model: EF does not track a
            // DB-side default it was not told about, so this cannot provoke a spurious follow-up
            // migration, and the model stays free of configuration for a legacy entity that has none.
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Discounts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<byte>(
                name: "DaysOfWeekMask",
                table: "Discounts",
                type: "tinyint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidFromUtc",
                table: "Discounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidToUtc",
                table: "Discounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WindowEndLocal",
                table: "Discounts",
                type: "time(6)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WindowStartLocal",
                table: "Discounts",
                type: "time(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Active",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "DaysOfWeekMask",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "ValidFromUtc",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "ValidToUtc",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "WindowEndLocal",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "WindowStartLocal",
                table: "Discounts");
        }
    }
}
