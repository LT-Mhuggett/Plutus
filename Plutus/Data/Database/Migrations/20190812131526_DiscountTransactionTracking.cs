using Microsoft.EntityFrameworkCore.Migrations;

namespace Database.Migrations
{
    public partial class DiscountTransactionTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalExTax",
                table: "Sales",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Transaction_Discounts",
                columns: table => new
                {
                    TransactionId = table.Column<int>(nullable: false),
                    DiscountId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transaction_Discounts", x => new { x.TransactionId, x.DiscountId });
                    table.ForeignKey(
                        name: "FK_Transaction_Discounts_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Transaction_Discounts_Trans_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Trans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_Discounts_DiscountId",
                table: "Transaction_Discounts",
                column: "DiscountId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transaction_Discounts");

            migrationBuilder.DropColumn(
                name: "TotalExTax",
                table: "Sales");
        }
    }
}
