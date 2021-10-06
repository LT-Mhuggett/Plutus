using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class RenameBussinessToBusiness : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Discounts_Bussiness_BussinessId",
                table: "Discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Bussiness_BussinessId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Bussiness_IdTwo",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Bussiness_BussinessId",
                table: "Stores");

            migrationBuilder.DropForeignKey(
                name: "FK_Taxes_Bussiness_IdTwo",
                table: "Taxes");

            migrationBuilder.DropTable(
                name: "Bussiness");

            migrationBuilder.DropIndex(
                name: "IX_Employees_BussinessId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_ObjectId_BussinessId_Email",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BussinessId",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "BussinessId",
                table: "Stores",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Stores_BussinessId",
                table: "Stores",
                newName: "IX_Stores_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BussinessId",
                table: "Discounts",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Discounts_BussinessId",
                table: "Discounts",
                newName: "IX_Discounts_BusinessId");

            migrationBuilder.AddColumn<string>(
                name: "BusinessId",
                table: "Employees",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Business",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VatIN = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameAbbr = table.Column<string>(type: "longtext", nullable: false)
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
                    table.PrimaryKey("PK_Business", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BusinessId",
                table: "Employees",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ObjectId_BusinessId_Email",
                table: "Employees",
                columns: new[] { "ObjectId", "BusinessId", "Email" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Discounts_Business_BusinessId",
                table: "Discounts",
                column: "BusinessId",
                principalTable: "Business",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Business_BusinessId",
                table: "Employees",
                column: "BusinessId",
                principalTable: "Business",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Business_IdTwo",
                table: "Items",
                column: "IdTwo",
                principalTable: "Business",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Business_BusinessId",
                table: "Stores",
                column: "BusinessId",
                principalTable: "Business",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Taxes_Business_IdTwo",
                table: "Taxes",
                column: "IdTwo",
                principalTable: "Business",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Discounts_Business_BusinessId",
                table: "Discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Business_BusinessId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Business_IdTwo",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Business_BusinessId",
                table: "Stores");

            migrationBuilder.DropForeignKey(
                name: "FK_Taxes_Business_IdTwo",
                table: "Taxes");

            migrationBuilder.DropTable(
                name: "Business");

            migrationBuilder.DropIndex(
                name: "IX_Employees_BusinessId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_ObjectId_BusinessId_Email",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Stores",
                newName: "BussinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Stores_BusinessId",
                table: "Stores",
                newName: "IX_Stores_BussinessId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Discounts",
                newName: "BussinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Discounts_BusinessId",
                table: "Discounts",
                newName: "IX_Discounts_BussinessId");

            migrationBuilder.AddColumn<string>(
                name: "BussinessId",
                table: "Employees",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Bussiness",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Logo = table.Column<byte[]>(type: "longblob", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ModifiedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameAbbr = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecMarkup = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    VatIN = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bussiness", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BussinessId",
                table: "Employees",
                column: "BussinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ObjectId_BussinessId_Email",
                table: "Employees",
                columns: new[] { "ObjectId", "BussinessId", "Email" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Discounts_Bussiness_BussinessId",
                table: "Discounts",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Bussiness_BussinessId",
                table: "Employees",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Bussiness_IdTwo",
                table: "Items",
                column: "IdTwo",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Bussiness_BussinessId",
                table: "Stores",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Taxes_Bussiness_IdTwo",
                table: "Taxes",
                column: "IdTwo",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
