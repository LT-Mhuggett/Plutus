using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    public partial class UpdateTransaction_DiscountKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Transaction_Discounts",
                table: "Transaction_Discounts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Transaction_Discounts",
                table: "Transaction_Discounts",
                columns: new[] { "TransactionId", "DiscountId", "SaleId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Transaction_Discounts",
                table: "Transaction_Discounts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Transaction_Discounts",
                table: "Transaction_Discounts",
                columns: new[] { "TransactionId", "DiscountId" });
        }
    }
}
