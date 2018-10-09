using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Database.Migrations.SqliteMigrations
{
    public partial class v1_0_3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutItemChangeModel",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(nullable: true),
                    Price = table.Column<decimal>(nullable: false),
                    SaleId = table.Column<string>(nullable: true),
                    Created = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    Modified = table.Column<DateTime>(nullable: false),
                    ModifiedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutItemChangeModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutItemChangeModel_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckoutItemChangeModel_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItemChangeModel_ItemId",
                table: "CheckoutItemChangeModel",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItemChangeModel_SaleId",
                table: "CheckoutItemChangeModel",
                column: "SaleId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutItemChangeModel");
        }
    }
}
