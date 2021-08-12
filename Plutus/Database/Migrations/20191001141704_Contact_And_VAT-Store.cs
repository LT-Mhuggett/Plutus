using Microsoft.EntityFrameworkCore.Migrations;

namespace Database.Migrations
{
    public partial class Contact_And_VATStore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "Stores",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatIN",
                table: "Stores",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "VatIN",
                table: "Stores");
        }
    }
}
