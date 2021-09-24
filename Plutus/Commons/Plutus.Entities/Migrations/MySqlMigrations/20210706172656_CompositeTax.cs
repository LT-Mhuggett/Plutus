using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class CompositeTax : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Vats_VatId",
                table: "Items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Vats",
                table: "Vats");

            migrationBuilder.DropIndex(
                name: "IX_Items_VatId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Vats");

            migrationBuilder.AddColumn<Guid>(
                name: "IdOne",
                table: "Vats",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "IdTwo",
                table: "Vats",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<Guid>(
                name: "VatId",
                table: "Items",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Vats",
                table: "Vats",
                columns: new[] { "IdOne", "IdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Vats_IdTwo",
                table: "Vats",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_Items_VatId_IdTwo",
                table: "Items",
                columns: new[] { "VatId", "IdTwo" });

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

        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropIndex(
                name: "IX_Vats_IdTwo",
                table: "Vats");

            migrationBuilder.DropIndex(
                name: "IX_Items_VatId_IdTwo",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IdOne",
                table: "Vats");

            migrationBuilder.DropColumn(
                name: "IdTwo",
                table: "Vats");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Vats",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AlterColumn<int>(
                name: "VatId",
                table: "Items",
                type: "int",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Vats",
                table: "Vats",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Items_VatId",
                table: "Items",
                column: "VatId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Vats_VatId",
                table: "Items",
                column: "VatId",
                principalTable: "Vats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
