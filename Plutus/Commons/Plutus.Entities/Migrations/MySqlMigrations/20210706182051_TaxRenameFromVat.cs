using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class TaxRenameFromVat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Vats_VatId_IdTwo",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Vats_Bussiness_IdTwo",
                table: "Vats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Vats",
                table: "Vats");

            migrationBuilder.RenameTable(
                name: "Vats",
                newName: "Taxes");

            migrationBuilder.RenameColumn(
                name: "VatId",
                table: "Items",
                newName: "TaxId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_VatId_IdTwo",
                table: "Items",
                newName: "IX_Items_TaxId_IdTwo");

            migrationBuilder.RenameIndex(
                name: "IX_Vats_IdTwo",
                table: "Taxes",
                newName: "IX_Taxes_IdTwo");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Taxes",
                table: "Taxes",
                columns: new[] { "IdOne", "IdTwo" });

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Taxes_TaxId_IdTwo",
                table: "Items",
                columns: new[] { "TaxId", "IdTwo" },
                principalTable: "Taxes",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Taxes_Bussiness_IdTwo",
                table: "Taxes",
                column: "IdTwo",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Taxes_TaxId_IdTwo",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Taxes_Bussiness_IdTwo",
                table: "Taxes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Taxes",
                table: "Taxes");

            migrationBuilder.RenameTable(
                name: "Taxes",
                newName: "Vats");

            migrationBuilder.RenameColumn(
                name: "TaxId",
                table: "Items",
                newName: "VatId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_TaxId_IdTwo",
                table: "Items",
                newName: "IX_Items_VatId_IdTwo");

            migrationBuilder.RenameIndex(
                name: "IX_Taxes_IdTwo",
                table: "Vats",
                newName: "IX_Vats_IdTwo");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Vats",
                table: "Vats",
                columns: new[] { "IdOne", "IdTwo" });

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Vats_VatId_IdTwo",
                table: "Items",
                columns: new[] { "VatId", "IdTwo" },
                principalTable: "Vats",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Vats_Bussiness_IdTwo",
                table: "Vats",
                column: "IdTwo",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
