using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectableCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectableCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectableCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectableCollections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectableCollectionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectableCollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectableCollectionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectableCollectionItems_CollectableCollections_CollectableCollectionId",
                        column: x => x.CollectableCollectionId,
                        principalTable: "CollectableCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectableCollectionItems_Collectables_CollectableId",
                        column: x => x.CollectableId,
                        principalTable: "Collectables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectableCollectionItems_CollectableCollectionId",
                table: "CollectableCollectionItems",
                column: "CollectableCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectableCollectionItems_CollectableId",
                table: "CollectableCollectionItems",
                column: "CollectableId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectableCollections_UserId",
                table: "CollectableCollections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectableCollectionItems");

            migrationBuilder.DropTable(
                name: "CollectableCollections");
        }
    }
}
