using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class CreateBussiness : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Logo",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "RecMarkup",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "StoreAbbr",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "StoreName",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "VatIN",
                table: "Stores");

            migrationBuilder.AddColumn<string>(
                name: "BussinessId",
                table: "Stores",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StoreId",
                table: "Sales",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BussinessId",
                table: "Items",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Bussiness",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VatIN = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameAbbr = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Logo = table.Column<byte[]>(type: "longblob", nullable: true),
                    RecMarkup = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModifiedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bussiness", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_BussinessId",
                table: "Stores",
                column: "BussinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StoreId",
                table: "Sales",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BussinessId",
                table: "Items",
                column: "BussinessId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Bussiness_BussinessId",
                table: "Items",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Stores_StoreId",
                table: "Sales",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Bussiness_BussinessId",
                table: "Stores",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Bussiness_BussinessId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Stores_StoreId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Bussiness_BussinessId",
                table: "Stores");

            migrationBuilder.DropTable(
                name: "Bussiness");

            migrationBuilder.DropIndex(
                name: "IX_Stores_BussinessId",
                table: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Sales_StoreId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Items_BussinessId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "BussinessId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BussinessId",
                table: "Items");

            migrationBuilder.AddColumn<byte[]>(
                name: "Logo",
                table: "Stores",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecMarkup",
                table: "Stores",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreAbbr",
                table: "Stores",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StoreName",
                table: "Stores",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VatIN",
                table: "Stores",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
