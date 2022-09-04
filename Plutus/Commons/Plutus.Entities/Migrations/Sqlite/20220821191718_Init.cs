using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plutus.Entities.Migrations.Sqlite
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Module = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Business",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Logo = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameAbbr = table.Column<string>(type: "TEXT", nullable: false),
                    RecMarkup = table.Column<decimal>(type: "TEXT", nullable: true),
                    VatIN = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Business", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DbActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeName = table.Column<string>(type: "TEXT", nullable: false),
                    RecordId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Charge = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinimumCharge = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsChangeable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCashBackable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    FName = table.Column<string>(type: "TEXT", nullable: false),
                    LName = table.Column<string>(type: "TEXT", nullable: false),
                    Mobile = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false),
                    AdLine1 = table.Column<string>(type: "TEXT", nullable: false),
                    AdLine2 = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    PostCode = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    FullAddress = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    IdOne = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => new { x.IdOne, x.IdTwo });
                    table.ForeignKey(
                        name: "FK_Category_Business_IdTwo",
                        column: x => x.IdTwo,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Discounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AllApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanUseWithOtherDiscounts = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoApply = table.Column<bool>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    UsesPerTransaction = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredNumOfItems = table.Column<int>(type: "INTEGER", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Discounts_Business_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentRoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Role_Business_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Role_Role_ParentRoleId",
                        column: x => x.ParentRoleId,
                        principalTable: "Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContactNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false),
                    AdLine1 = table.Column<string>(type: "TEXT", nullable: false),
                    AdLine2 = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    PostCode = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    FullAddress = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stores_Business_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Taxes",
                columns: table => new
                {
                    IdOne = table.Column<int>(type: "INTEGER", nullable: false),
                    IdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Taxes", x => new { x.IdOne, x.IdTwo });
                    table.ForeignKey(
                        name: "FK_Taxes_Business_IdTwo",
                        column: x => x.IdTwo,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscountCats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CatIdOne = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatIdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscountId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountCats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountCats_Category_CatIdOne_CatIdTwo",
                        columns: x => new { x.CatIdOne, x.CatIdTwo },
                        principalTable: "Category",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscountCats_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthActionAPIMapping",
                columns: table => new
                {
                    IdOne = table.Column<int>(type: "INTEGER", nullable: false),
                    IdTwo = table.Column<int>(type: "INTEGER", nullable: false),
                    Module = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthActionAPIMapping", x => new { x.IdOne, x.IdTwo });
                    table.ForeignKey(
                        name: "FK_AuthActionAPIMapping_AuthActions_IdTwo",
                        column: x => x.IdTwo,
                        principalTable: "AuthActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuthActionAPIMapping_Role_IdOne",
                        column: x => x.IdOne,
                        principalTable: "Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContractedHours = table.Column<int>(type: "INTEGER", nullable: false),
                    NIN = table.Column<string>(type: "TEXT", nullable: false),
                    Wage = table.Column<decimal>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ManagedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Business_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Employees_Employees_ManagedById",
                        column: x => x.ManagedById,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Employees_People_Id",
                        column: x => x.Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Employees_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Employees_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Till",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    CashFloat = table.Column<decimal>(type: "TEXT", nullable: false),
                    LastOnline = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Till_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    IdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdOne = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", nullable: false),
                    Desc = table.Column<string>(type: "TEXT", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    Image = table.Column<byte[]>(type: "BLOB", nullable: false),
                    TaxId = table.Column<int>(type: "INTEGER", nullable: false),
                    CatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => new { x.IdOne, x.IdTwo });
                    table.ForeignKey(
                        name: "FK_Items_Business_IdTwo",
                        column: x => x.IdTwo,
                        principalTable: "Business",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Items_Category_CatId_IdTwo",
                        columns: x => new { x.CatId, x.IdTwo },
                        principalTable: "Category",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Items_Taxes_TaxId_IdTwo",
                        columns: x => new { x.TaxId, x.IdTwo },
                        principalTable: "Taxes",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmpAuthActions",
                columns: table => new
                {
                    AuthAId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmpId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Permissions = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmpAuthActions", x => new { x.AuthAId, x.EmpId });
                    table.ForeignKey(
                        name: "FK_EmpAuthActions_AuthActions_AuthAId",
                        column: x => x.AuthAId,
                        principalTable: "AuthActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmpAuthActions_Employees_EmpId",
                        column: x => x.EmpId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalExTax = table.Column<decimal>(type: "TEXT", nullable: false),
                    DateOfSale = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    TillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sales_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sales_Till_TillId",
                        column: x => x.TillId,
                        principalTable: "Till",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutItemChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    ItemIdOne = table.Column<string>(type: "TEXT", nullable: false),
                    ItemIdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutItemChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutItemChange_Items_ItemIdOne_ItemIdTwo",
                        columns: x => new { x.ItemIdOne, x.ItemIdTwo },
                        principalTable: "Items",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscountItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ItemIdOne = table.Column<string>(type: "TEXT", nullable: false),
                    ItemIdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscountId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountItems_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscountItems_Items_ItemIdOne_ItemIdTwo",
                        columns: x => new { x.ItemIdOne, x.ItemIdTwo },
                        principalTable: "Items",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stocks",
                columns: table => new
                {
                    IdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdThree = table.Column<int>(type: "INTEGER", nullable: false),
                    IdOne = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocks", x => new { x.IdOne, x.IdTwo, x.IdThree });
                    table.ForeignKey(
                        name: "FK_Stocks_Items_IdOne_IdTwo",
                        columns: x => new { x.IdOne, x.IdTwo },
                        principalTable: "Items",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Stocks_Stores_IdThree",
                        column: x => x.IdThree,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    IdOne = table.Column<int>(type: "INTEGER", nullable: false),
                    IdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => new { x.IdOne, x.IdTwo });
                    table.ForeignKey(
                        name: "FK_Notes_Sales_IdTwo",
                        column: x => x.IdTwo,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaySales",
                columns: table => new
                {
                    PayId = table.Column<int>(type: "INTEGER", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Change = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaySales", x => new { x.PayId, x.SaleId });
                    table.ForeignKey(
                        name: "FK_PaySales_PayMethods_PayId",
                        column: x => x.PayId,
                        principalTable: "PayMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaySales_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemIdOne = table.Column<string>(type: "TEXT", nullable: false),
                    ItemIdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthoriserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleIdReturned = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckoutItemChangeId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Refunds_CheckoutItemChange_CheckoutItemChangeId",
                        column: x => x.CheckoutItemChangeId,
                        principalTable: "CheckoutItemChange",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Refunds_Employees_AuthoriserId",
                        column: x => x.AuthoriserId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Refunds_Items_ItemIdOne_ItemIdTwo",
                        columns: x => new { x.ItemIdOne, x.ItemIdTwo },
                        principalTable: "Items",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Refunds_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Refunds_Sales_SaleIdReturned",
                        column: x => x.SaleIdReturned,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trans",
                columns: table => new
                {
                    IdOne = table.Column<int>(type: "INTEGER", nullable: false),
                    IdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemCostExPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    ItemCostPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    ItemIdOne = table.Column<string>(type: "TEXT", nullable: false),
                    ItemIdTwo = table.Column<Guid>(type: "TEXT", nullable: false),
                    TillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckoutItemChangeId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trans", x => new { x.IdOne, x.IdTwo });
                    table.ForeignKey(
                        name: "FK_Trans_CheckoutItemChange_CheckoutItemChangeId",
                        column: x => x.CheckoutItemChangeId,
                        principalTable: "CheckoutItemChange",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Trans_Items_ItemIdOne_ItemIdTwo",
                        columns: x => new { x.ItemIdOne, x.ItemIdTwo },
                        principalTable: "Items",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trans_Sales_IdTwo",
                        column: x => x.IdTwo,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trans_Till_TillId",
                        column: x => x.TillId,
                        principalTable: "Till",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transaction_Discounts",
                columns: table => new
                {
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscountId = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscountRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transaction_Discounts", x => new { x.TransactionId, x.DiscountId });
                    table.ForeignKey(
                        name: "FK_Transaction_Discounts_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Transaction_Discounts_Trans_TransactionId_SaleId",
                        columns: x => new { x.TransactionId, x.SaleId },
                        principalTable: "Trans",
                        principalColumns: new[] { "IdOne", "IdTwo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthActionAPIMapping_IdTwo",
                table: "AuthActionAPIMapping",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_Category_IdTwo",
                table: "Category",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItemChange_ItemIdOne_ItemIdTwo",
                table: "CheckoutItemChange",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountCats_CatIdOne_CatIdTwo",
                table: "DiscountCats",
                columns: new[] { "CatIdOne", "CatIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountCats_DiscountId",
                table: "DiscountCats",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountItems_DiscountId",
                table: "DiscountItems",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountItems_ItemIdOne_ItemIdTwo",
                table: "DiscountItems",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Discounts_BusinessId",
                table: "Discounts",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EmpAuthActions_EmpId",
                table: "EmpAuthActions",
                column: "EmpId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BusinessId",
                table: "Employees",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Id_BusinessId",
                table: "Employees",
                columns: new[] { "Id", "BusinessId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ManagedById",
                table: "Employees",
                column: "ManagedById");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_RoleId",
                table: "Employees",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_StoreId",
                table: "Employees",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CatId_IdTwo",
                table: "Items",
                columns: new[] { "CatId", "IdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_IdTwo",
                table: "Items",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_Items_TaxId_IdTwo",
                table: "Items",
                columns: new[] { "TaxId", "IdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_IdTwo",
                table: "Notes",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_PaySales_SaleId",
                table: "PaySales",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_AuthoriserId",
                table: "Refunds",
                column: "AuthoriserId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_CheckoutItemChangeId",
                table: "Refunds",
                column: "CheckoutItemChangeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ItemIdOne_ItemIdTwo",
                table: "Refunds",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_SaleId",
                table: "Refunds",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_SaleIdReturned",
                table: "Refunds",
                column: "SaleIdReturned");

            migrationBuilder.CreateIndex(
                name: "IX_Role_BusinessId",
                table: "Role",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Role_ParentRoleId",
                table: "Role",
                column: "ParentRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_EmployeeId",
                table: "Sales",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StoreId",
                table: "Sales",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TillId",
                table: "Sales",
                column: "TillId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_IdOne_IdTwo",
                table: "Stocks",
                columns: new[] { "IdOne", "IdTwo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_IdThree",
                table: "Stocks",
                column: "IdThree");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_BusinessId",
                table: "Stores",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Taxes_IdTwo",
                table: "Taxes",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_Till_StoreId",
                table: "Till",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Trans_CheckoutItemChangeId",
                table: "Trans",
                column: "CheckoutItemChangeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trans_IdTwo",
                table: "Trans",
                column: "IdTwo");

            migrationBuilder.CreateIndex(
                name: "IX_Trans_ItemIdOne_ItemIdTwo",
                table: "Trans",
                columns: new[] { "ItemIdOne", "ItemIdTwo" });

            migrationBuilder.CreateIndex(
                name: "IX_Trans_TillId",
                table: "Trans",
                column: "TillId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_Discounts_DiscountId",
                table: "Transaction_Discounts",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_Discounts_TransactionId_SaleId",
                table: "Transaction_Discounts",
                columns: new[] { "TransactionId", "SaleId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthActionAPIMapping");

            migrationBuilder.DropTable(
                name: "DbActions");

            migrationBuilder.DropTable(
                name: "DiscountCats");

            migrationBuilder.DropTable(
                name: "DiscountItems");

            migrationBuilder.DropTable(
                name: "EmpAuthActions");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "PaySales");

            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "SavedTransactions");

            migrationBuilder.DropTable(
                name: "Stocks");

            migrationBuilder.DropTable(
                name: "Transaction_Discounts");

            migrationBuilder.DropTable(
                name: "AuthActions");

            migrationBuilder.DropTable(
                name: "PayMethods");

            migrationBuilder.DropTable(
                name: "Discounts");

            migrationBuilder.DropTable(
                name: "Trans");

            migrationBuilder.DropTable(
                name: "CheckoutItemChange");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Till");

            migrationBuilder.DropTable(
                name: "Category");

            migrationBuilder.DropTable(
                name: "Taxes");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Role");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Business");
        }
    }
}
