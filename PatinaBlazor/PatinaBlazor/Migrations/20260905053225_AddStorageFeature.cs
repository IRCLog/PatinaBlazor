using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageProperties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageProperties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageProperties_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageProperties_AspNetUsers_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoragePropertyImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoragePropertyId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IsMainImage = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoragePropertyImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoragePropertyImages_StorageProperties_StoragePropertyId",
                        column: x => x.StoragePropertyId,
                        principalTable: "StorageProperties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoragePropertyId = table.Column<int>(type: "int", nullable: false),
                    UnitNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LengthFeet = table.Column<decimal>(type: "decimal(6,2)", nullable: false),
                    WidthFeet = table.Column<decimal>(type: "decimal(6,2)", nullable: false),
                    HeightFeet = table.Column<decimal>(type: "decimal(6,2)", nullable: false),
                    MonthlyRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageUnits_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageUnits_AspNetUsers_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageUnits_StorageProperties_StoragePropertyId",
                        column: x => x.StoragePropertyId,
                        principalTable: "StorageProperties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageRentals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorageUnitId = table.Column<int>(type: "int", nullable: false),
                    CustomerUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MonthlyRateAtSigning = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageRentals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageRentals_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageRentals_AspNetUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageRentals_AspNetUsers_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageRentals_StorageUnits_StorageUnitId",
                        column: x => x.StorageUnitId,
                        principalTable: "StorageUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageProperties_CreatedByUserId",
                table: "StorageProperties",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageProperties_ModifiedByUserId",
                table: "StorageProperties",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoragePropertyImages_StoragePropertyId",
                table: "StoragePropertyImages",
                column: "StoragePropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageRentals_CreatedByUserId",
                table: "StorageRentals",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageRentals_CustomerUserId",
                table: "StorageRentals",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageRentals_ModifiedByUserId",
                table: "StorageRentals",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageRentals_StorageUnitId",
                table: "StorageRentals",
                column: "StorageUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageUnits_CreatedByUserId",
                table: "StorageUnits",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageUnits_ModifiedByUserId",
                table: "StorageUnits",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageUnits_StoragePropertyId",
                table: "StorageUnits",
                column: "StoragePropertyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoragePropertyImages");

            migrationBuilder.DropTable(
                name: "StorageRentals");

            migrationBuilder.DropTable(
                name: "StorageUnits");

            migrationBuilder.DropTable(
                name: "StorageProperties");
        }
    }
}
