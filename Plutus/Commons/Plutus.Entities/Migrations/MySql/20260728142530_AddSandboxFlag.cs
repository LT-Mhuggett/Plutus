using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddSandboxFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSandbox",
                table: "Tenants",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSandbox",
                table: "Tenants");
        }
    }
}
