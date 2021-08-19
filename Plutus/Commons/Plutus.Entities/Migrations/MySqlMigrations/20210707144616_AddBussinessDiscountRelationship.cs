using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class AddBussinessDiscountRelationship : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BussinessId",
                table: "Discounts",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Discounts_BussinessId",
                table: "Discounts",
                column: "BussinessId");

            migrationBuilder.AddForeignKey(
                name: "FK_Discounts_Bussiness_BussinessId",
                table: "Discounts",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Discounts_Bussiness_BussinessId",
                table: "Discounts");

            migrationBuilder.DropIndex(
                name: "IX_Discounts_BussinessId",
                table: "Discounts");

            migrationBuilder.DropColumn(
                name: "BussinessId",
                table: "Discounts");
        }
    }
}
