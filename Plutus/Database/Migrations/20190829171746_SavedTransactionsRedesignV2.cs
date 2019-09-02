using Microsoft.EntityFrameworkCore.Migrations;

namespace Database.Migrations
{
    public partial class SavedTransactionsRedesignV2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "SavedTransactions",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "SavedTransactions");
        }
    }
}
