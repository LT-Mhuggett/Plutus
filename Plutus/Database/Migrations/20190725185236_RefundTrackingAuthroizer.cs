using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace Database.Migrations
{
    public partial class RefundTrackingAuthroizer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.CreateTable(
                    name: "NEW_Refunds",
                    columns: table => new
                    {
                        Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                        Reason = table.Column<string>(nullable: true),
                        SaleIdReturned = table.Column<string>(nullable: true),
                        SaleId = table.Column<string>(nullable: true),
                        ItemId = table.Column<string>(nullable: true),
                        Amount = table.Column<int>(nullable: false),
                        AuthoriserId = table.Column<string>(nullable: true),
                        CheckoutItemChangeId = table.Column<int>(nullable: true),
                        Created = table.Column<DateTime>(nullable: false),
                        CreatedBy = table.Column<string>(nullable: true),
                        Modified = table.Column<DateTime>(nullable: false),
                        ModifiedBy = table.Column<string>(nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Refunds", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Refunds_CheckoutItemChangeModel_CheckoutItemChangeId",
                            column: x => x.CheckoutItemChangeId,
                            principalTable: "CheckoutItemChangeModel",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Items_ItemId",
                            column: x => x.ItemId,
                            principalTable: "Items",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Employees_AuthoriserId",
                            column: x => x.AuthoriserId,
                            principalTable: "Employees",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Sales_SaleId",
                            column: x => x.SaleId,
                            principalTable: "Sales",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Sales_SaleIdReturned",
                            column: x => x.SaleIdReturned,
                            principalTable: "Sales",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                    });
                migrationBuilder.Sql("PRAGMA foreign_keys=\"0\"", true);
                migrationBuilder.Sql("Insert INTO NEW_Refunds ([Id], [Reason], [SaleIdReturned], [SaleId], [ItemId], [Amount], [CheckoutItemChangeId], [Created], [CreatedBy], [Modified], [ModifiedBy], [AuthoriserId]) " +
                    "SELECT Id, Reason, SaleIdReturned, SaleId, ItemId, Amount, CheckoutItemChangeId, Created, CreatedBy, Modified, ModifiedBy, NULL " +
                    "FROM Refunds");
                migrationBuilder.Sql("DROP TABLE Refunds", true);
                migrationBuilder.Sql("ALTER TABLE NEW_Refunds RENAME TO Refunds", true);
                migrationBuilder.Sql("PRAGMA foreign_keys=\"1\"", true);
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "AuthoriserId",
                    table: "Refunds",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_Refunds_AuthoriserId",
                    table: "Refunds",
                    column: "AuthoriserId");

                migrationBuilder.AddForeignKey(
                    name: "FK_Refunds_Employees_AuthoriserId",
                    table: "Refunds",
                    column: "AuthoriserId",
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.CreateTable(
                    name: "NEW_Refunds",
                    columns: table => new
                    {
                        Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                        Reason = table.Column<string>(nullable: true),
                        SaleIdReturned = table.Column<string>(nullable: true),
                        SaleId = table.Column<string>(nullable: true),
                        ItemId = table.Column<string>(nullable: true),
                        Amount = table.Column<int>(nullable: false),
                        CheckoutItemChangeId = table.Column<int>(nullable: true),
                        Created = table.Column<DateTime>(nullable: false),
                        CreatedBy = table.Column<string>(nullable: true),
                        Modified = table.Column<DateTime>(nullable: false),
                        ModifiedBy = table.Column<string>(nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Refunds", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Refunds_CheckoutItemChangeModel_CheckoutItemChangeId",
                            column: x => x.CheckoutItemChangeId,
                            principalTable: "CheckoutItemChangeModel",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Items_ItemId",
                            column: x => x.ItemId,
                            principalTable: "Items",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Sales_SaleId",
                            column: x => x.SaleId,
                            principalTable: "Sales",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_Refunds_Sales_SaleIdReturned",
                            column: x => x.SaleIdReturned,
                            principalTable: "Sales",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                    });
                migrationBuilder.Sql("PRAGMA foreign_keys=\"0\"", true);
                migrationBuilder.Sql("Insert INTO NEW_Refunds SELECT " +
                    "Id, Reason, SaleIdReturned, SaleId, ItemId, Amount, CheckoutItemChangeId, Created, CreatedBy, Modified, ModifiedBy " +
                    "FROM Refunds");
                migrationBuilder.Sql("DROP TABLE Refunds", true);
                migrationBuilder.Sql("ALTER TABLE NEW_Refunds RENAME TO Refunds", true);
                migrationBuilder.Sql("PRAGMA foreign_keys=\"1\"", true);
            }
            else
            {

                migrationBuilder.DropForeignKey(
                    name: "FK_Refunds_Employees_AuthoriserId",
                    table: "Refunds");

                migrationBuilder.DropIndex(
                    name: "IX_Refunds_AuthoriserId",
                    table: "Refunds");

                migrationBuilder.DropColumn(
                    name: "AuthoriserId",
                    table: "Refunds");
            }
        }
    }
}
