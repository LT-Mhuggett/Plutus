using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CatchUpSqliteSchemaWithFullModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Role_Role_ParentRoleId",
                table: "Role");

            migrationBuilder.DropIndex(
                name: "IX_Role_ParentRoleId",
                table: "Role");

            migrationBuilder.DropColumn(
                name: "ParentRoleId",
                table: "Role");

            migrationBuilder.AlterColumn<int>(
                name: "ParentId",
                table: "Role",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_Role_ParentId",
                table: "Role",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Role_Role_ParentId",
                table: "Role",
                column: "ParentId",
                principalTable: "Role",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Role_Role_ParentId",
                table: "Role");

            migrationBuilder.DropIndex(
                name: "IX_Role_ParentId",
                table: "Role");

            migrationBuilder.AlterColumn<int>(
                name: "ParentId",
                table: "Role",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentRoleId",
                table: "Role",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Role_ParentRoleId",
                table: "Role",
                column: "ParentRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Role_Role_ParentRoleId",
                table: "Role",
                column: "ParentRoleId",
                principalTable: "Role",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
