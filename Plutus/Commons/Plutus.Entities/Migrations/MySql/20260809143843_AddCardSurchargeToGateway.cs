using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddCardSurchargeToGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SurchargeBp",
                table: "PaymentGatewaySettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SurchargeFlatPence",
                table: "PaymentGatewaySettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SurchargeBp",
                table: "PaymentGatewaySettings");

            migrationBuilder.DropColumn(
                name: "SurchargeFlatPence",
                table: "PaymentGatewaySettings");
        }
    }
}
