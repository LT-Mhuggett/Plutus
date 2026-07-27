using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddRbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RbacRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsBuiltIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RbacRoles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RbacRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ScopeType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ScopeId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DaysOfWeekMask = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                    WindowStartLocal = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    WindowEndLocal = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ValidToUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RbacRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RbacRoleAssignments_RbacRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "RbacRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RbacRoleGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PermissionCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxPence = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RbacRoleGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RbacRoleGrants_RbacRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "RbacRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoleAssignments_RoleId",
                table: "RbacRoleAssignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoleAssignments_TenantId",
                table: "RbacRoleAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoleAssignments_TenantId_ScopeType_ScopeId",
                table: "RbacRoleAssignments",
                columns: new[] { "TenantId", "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoleAssignments_TenantId_UserId",
                table: "RbacRoleAssignments",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoleGrants_RoleId_PermissionCode",
                table: "RbacRoleGrants",
                columns: new[] { "RoleId", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoleGrants_TenantId",
                table: "RbacRoleGrants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoles_TenantId",
                table: "RbacRoles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RbacRoles_TenantId_Name",
                table: "RbacRoles",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RbacRoleAssignments");

            migrationBuilder.DropTable(
                name: "RbacRoleGrants");

            migrationBuilder.DropTable(
                name: "RbacRoles");
        }
    }
}
