using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.MySqlMigrations
{
    public partial class CompositeBaseKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckoutItemChange_Items_ItemId",
                table: "CheckoutItemChange");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountItems_Items_ItemId",
                table: "DiscountItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Bussiness_BussinessId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_Items_ItemId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Items_ItemId",
                table: "Stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_Trans_Items_ItemId",
                table: "Trans");

            migrationBuilder.DropIndex(
                name: "IX_Trans_ItemId",
                table: "Trans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Stocks",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ItemId",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_ItemId",
                table: "Refunds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Items",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_BussinessId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_DiscountItems_ItemId",
                table: "DiscountItems");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutItemChange_ItemId",
                table: "CheckoutItemChange");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "Trans",
                newName: "ItemIdTwo");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "Stocks",
                newName: "ItemIdTwo");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "Refunds",
                newName: "ItemIdTwo");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Items",
                newName: "IdTwo");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "DiscountItems",
                newName: "ItemIdTwo");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "CheckoutItemChange",
                newName: "ItemIdTwo");

            migrationBuilder.AddColumn<string>(
                name: "ItemIdOne",
                table: "Trans",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ItemIdOne",
                table: "Stocks",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ItemIdOne",
                table: "Refunds",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "BussinessId",
                table: "Items",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IdOne",
                table: "Items",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BussinessId",
                table: "Employees",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ItemIdOne",
                table: "DiscountItems",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ItemIdOne",
                table: "CheckoutItemChange",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Stocks",
                table: "Stocks",
                columns: new[] { "ItemIdOne", "ItemIdTwo", "StoreId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Items",
                table: "Items",
                columns: new[] { "IdOne", "IdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Trans_ItemIdOne_ItemIdTwo",
                table: "Trans",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ItemIdOne_ItemIdTwo",
                table: "Stocks",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ItemIdOne_ItemIdTwo",
                table: "Refunds",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_IdTwo",
                table: "Items",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BussinessId",
                table: "Employees",
                column: "BussinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountItems_ItemIdOne_ItemIdTwo",
                table: "DiscountItems",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItemChange_ItemIdOne_ItemIdTwo",
                table: "CheckoutItemChange",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.AddForeignKey(
                name: "FK_CheckoutItemChange_Items_ItemIdOne_ItemIdTwo",
                table: "CheckoutItemChange",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountItems_Items_ItemIdOne_ItemIdTwo",
                table: "DiscountItems",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Restrict);

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
                name: "FK_Refunds_Items_ItemIdOne_ItemIdTwo",
                table: "Refunds",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Items_ItemIdOne_ItemIdTwo",
                table: "Stocks",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Trans_Items_ItemIdOne_ItemIdTwo",
                table: "Trans",
                columns: new[] { "ItemIdOne", "ItemIdTwo" },
                principalTable: "Items",
                principalColumns: new[] { "IdOne", "IdTwo" },
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckoutItemChange_Items_ItemIdOne_ItemIdTwo",
                table: "CheckoutItemChange");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountItems_Items_ItemIdOne_ItemIdTwo",
                table: "DiscountItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Bussiness_BussinessId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Bussiness_IdTwo",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_Items_ItemIdOne_ItemIdTwo",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Stocks_Items_ItemIdOne_ItemIdTwo",
                table: "Stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_Trans_Items_ItemIdOne_ItemIdTwo",
                table: "Trans");

            migrationBuilder.DropIndex(
                name: "IX_Trans_ItemIdOne_ItemIdTwo",
                table: "Trans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Stocks",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Stocks_ItemIdOne_ItemIdTwo",
                table: "Stocks");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_ItemIdOne_ItemIdTwo",
                table: "Refunds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Items",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_IdTwo",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Employees_BussinessId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_DiscountItems_ItemIdOne_ItemIdTwo",
                table: "DiscountItems");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutItemChange_ItemIdOne_ItemIdTwo",
                table: "CheckoutItemChange");

            migrationBuilder.DropColumn(
                name: "ItemIdOne",
                table: "Trans");

            migrationBuilder.DropColumn(
                name: "ItemIdOne",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "ItemIdOne",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "IdOne",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "BussinessId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ItemIdOne",
                table: "DiscountItems");

            migrationBuilder.DropColumn(
                name: "ItemIdOne",
                table: "CheckoutItemChange");

            migrationBuilder.RenameColumn(
                name: "ItemIdTwo",
                table: "Trans",
                newName: "ItemId");

            migrationBuilder.RenameColumn(
                name: "ItemIdTwo",
                table: "Stocks",
                newName: "ItemId");

            migrationBuilder.RenameColumn(
                name: "ItemIdTwo",
                table: "Refunds",
                newName: "ItemId");

            migrationBuilder.RenameColumn(
                name: "IdTwo",
                table: "Items",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "ItemIdTwo",
                table: "DiscountItems",
                newName: "ItemId");

            migrationBuilder.RenameColumn(
                name: "ItemIdTwo",
                table: "CheckoutItemChange",
                newName: "ItemId");

            migrationBuilder.AlterColumn<string>(
                name: "BussinessId",
                table: "Items",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Stocks",
                table: "Stocks",
                columns: new[] { "ItemId", "StoreId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Items",
                table: "Items",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Trans_ItemId",
                table: "Trans",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ItemId",
                table: "Stocks",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ItemId",
                table: "Refunds",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BussinessId",
                table: "Items",
                column: "BussinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountItems_ItemId",
                table: "DiscountItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItemChange_ItemId",
                table: "CheckoutItemChange",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_CheckoutItemChange_Items_ItemId",
                table: "CheckoutItemChange",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountItems_Items_ItemId",
                table: "DiscountItems",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Bussiness_BussinessId",
                table: "Items",
                column: "BussinessId",
                principalTable: "Bussiness",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_Items_ItemId",
                table: "Refunds",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocks_Items_ItemId",
                table: "Stocks",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Trans_Items_ItemId",
                table: "Trans",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
