using Microsoft.EntityFrameworkCore.Migrations;

namespace Database.Migrations.SqliteMigrations
{
    public partial class v1_0_2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllApplicable",
                table: "Discounts",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoApply",
                table: "Discounts",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanUseWithOtherDiscounts",
                table: "Discounts",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllApplicable",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "AutoApply",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "CanUseWithOtherDiscounts",
                table: "Discounts");
        }
    }
}
