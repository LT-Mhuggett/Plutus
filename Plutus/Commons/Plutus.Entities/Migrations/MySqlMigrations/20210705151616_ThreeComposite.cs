using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class ThreeComposite : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Items_ItemIdOne_ItemIdTwo",
                table: "Stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Stores_StoreId",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "BussinessId",
                table: "Items");

            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "Stocks",
                newName: "IdThree");

            migrationBuilder.RenameColumn(
                name: "ItemIdTwo",
                table: "Stocks",
                newName: "IdTwo");

            migrationBuilder.RenameColumn(
                name: "ItemIdOne",
                table: "Stocks",
                newName: "IdOne");

            migrationBuilder.RenameIndex(
                name: "IX_Stocks_StoreId",
                table: "Stocks",
                newName: "IX_Stocks_IdThree");

            migrationBuilder.RenameIndex(
                name: "IX_Stocks_ItemIdOne_ItemIdTwo",
                table: "Stocks",
                newName: "IX_Stocks_IdOne_IdTwo");

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Items_IdOne_IdTwo",
                table: "Stocks",
                columns: new[] { "IdOne", "IdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Stores_IdThree",
                table: "Stocks",
                column: "IdThree",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Items_IdOne_IdTwo",
                table: "Stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Stores_IdThree",
                table: "Stocks");

            migrationBuilder.RenameColumn(
                name: "IdThree",
                table: "Stocks",
                newName: "StoreId");

            migrationBuilder.RenameColumn(
                name: "IdTwo",
                table: "Stocks",
                newName: "ItemIdTwo");

            migrationBuilder.RenameColumn(
                name: "IdOne",
                table: "Stocks",
                newName: "ItemIdOne");

            migrationBuilder.RenameIndex(
                name: "IX_Stocks_IdThree",
                table: "Stocks",
                newName: "IX_Stocks_StoreId");

            migrationBuilder.RenameIndex(
                name: "IX_Stocks_IdOne_IdTwo",
                table: "Stocks",
                newName: "IX_Stocks_ItemIdOne_ItemIdTwo");

            migrationBuilder.AddColumn<string>(
                name: "BussinessId",
                table: "Items",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Items_ItemIdOne_ItemIdTwo",
                table: "Stocks",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Stores_StoreId",
                table: "Stocks",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
