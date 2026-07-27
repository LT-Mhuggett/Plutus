using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddTenantIdToTenantOwned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Transaction_Discounts",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Trans",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Till",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Taxes",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Stores",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Stocks",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SavedTransactions",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sales",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Refunds",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "People",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PaySales",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Notes",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Items",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Discounts",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DiscountItems",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DiscountCats",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CheckoutItemChange",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Category",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Business",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_Discounts_TenantId",
                table: "Transaction_Discounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Trans_TenantId",
                table: "Trans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_TenantId",
                table: "Till",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Taxes_TenantId",
                table: "Taxes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_TenantId",
                table: "Stores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_TenantId",
                table: "Stocks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedTransactions_TenantId",
                table: "SavedTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TenantId",
                table: "Sales",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId",
                table: "Refunds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_People_TenantId",
                table: "People",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaySales_TenantId",
                table: "PaySales",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_TenantId",
                table: "Notes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_TenantId",
                table: "Items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Discounts_TenantId",
                table: "Discounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountItems_TenantId",
                table: "DiscountItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountCats_TenantId",
                table: "DiscountCats",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItemChange_TenantId",
                table: "CheckoutItemChange",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Category_TenantId",
                table: "Category",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Business_TenantId",
                table: "Business",
                column: "TenantId");

            // Backfill every pre-existing row to the founding Kapow tenant (architecture §3;
            // spec T1.1 "backfill in the same migration"). New TenantId columns default to
            // Guid.Empty, which the query filter would hide — so stamp existing rows to Kapow.
            // One statement per table (Pomelo executes each Sql() as its own command).
            const string kapow = "0192b8a0-1a6f-7000-8000-000000000001";
            foreach (var table in new[]
            {
                "Business", "Stores", "Till", "Items", "Category", "Taxes",
                "Discounts", "DiscountCats", "DiscountItems", "Transaction_Discounts",
                "Sales", "Trans", "PaySales", "Refunds", "Notes", "SavedTransactions",
                "Stocks", "People", "CheckoutItemChange",
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE `{table}` SET `TenantId` = '{kapow}' WHERE `TenantId` = '00000000-0000-0000-0000-000000000000';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transaction_Discounts_TenantId",
                table: "Transaction_Discounts");

            migrationBuilder.DropIndex(
                name: "IX_Trans_TenantId",
                table: "Trans");

            migrationBuilder.DropIndex(
                name: "IX_Till_TenantId",
                table: "Till");

            migrationBuilder.DropIndex(
                name: "IX_Taxes_TenantId",
                table: "Taxes");

            migrationBuilder.DropIndex(
                name: "IX_Stores_TenantId",
                table: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_TenantId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_SavedTransactions_TenantId",
                table: "SavedTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Sales_TenantId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_TenantId",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_People_TenantId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_PaySales_TenantId",
                table: "PaySales");

            migrationBuilder.DropIndex(
                name: "IX_Notes_TenantId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Items_TenantId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Discounts_TenantId",
                table: "Discounts");

            migrationBuilder.DropIndex(
                name: "IX_DiscountItems_TenantId",
                table: "DiscountItems");

            migrationBuilder.DropIndex(
                name: "IX_DiscountCats_TenantId",
                table: "DiscountCats");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutItemChange_TenantId",
                table: "CheckoutItemChange");

            migrationBuilder.DropIndex(
                name: "IX_Category_TenantId",
                table: "Category");

            migrationBuilder.DropIndex(
                name: "IX_Business_TenantId",
                table: "Business");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Transaction_Discounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Trans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Till");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Taxes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SavedTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PaySales");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DiscountItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DiscountCats");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CheckoutItemChange");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Business");
        }
    }
}
