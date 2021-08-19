using Microsoft.EntityFrameworkCore.Migrations;

namespace Plutus.Entities.Migrations.SqliteMigrations
{
    public partial class SavedTransactionsRedesign : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedItems");

            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropTable(
                    name: "SavedTransactions");
                migrationBuilder.CreateTable(
                    name: "SavedTransactions",
                    columns: table => new
                    {
                        Id = table.Column<string>(nullable: false)
                            .Annotation("Sqlite:AutoIncrement", true),
                        Data = table.Column<string>(nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_SavedTransactions", x => x.Id);
                    });
            }
            else
            {
                migrationBuilder.RenameColumn(
                name: "Name",
                table: "SavedTransactions",
                newName: "Data");

                migrationBuilder.AlterColumn<string>(
                    name: "Id",
                    table: "SavedTransactions",
                    nullable: false,
                    oldClrType: typeof(int))
                    .OldAnnotation("Sqlite:Autoincrement", true);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.DropTable(
                    name: "SavedTransactions");
                migrationBuilder.CreateTable(
                name: "SavedTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedTransactions", x => x.Id);
                });
            }
            else
            {
                migrationBuilder.RenameColumn(
                name: "Data",
                table: "SavedTransactions",
                newName: "Name");

                migrationBuilder.AlterColumn<int>(
                    name: "Id",
                    table: "SavedTransactions",
                    nullable: false,
                    oldClrType: typeof(string))
                    .Annotation("Sqlite:Autoincrement", true);
            }
            migrationBuilder.CreateTable(
                name: "SavedItems",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Amount = table.Column<int>(nullable: false),
                    ItemId = table.Column<string>(nullable: true),
                    SavedTransId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavedItems_SavedTransactions_SavedTransId",
                        column: x => x.SavedTransId,
                        principalTable: "SavedTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedItems_ItemId",
                table: "SavedItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedItems_SavedTransId",
                table: "SavedItems",
                column: "SavedTransId");
        }
    }
}
