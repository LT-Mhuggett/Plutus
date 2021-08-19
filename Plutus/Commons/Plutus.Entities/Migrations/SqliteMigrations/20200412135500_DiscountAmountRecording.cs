using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.SqliteMigrations
{
    public partial class DiscountAmountRecording : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountRate",
                table: "Transaction_Discounts",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                @"UPDATE Transaction_Discounts SET DiscountRate = (SELECT Amount FROM Discounts WHERE Id = Transaction_Discounts.DiscountId)", true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountRate",
                table: "Transaction_Discounts");
        }
    }
}
