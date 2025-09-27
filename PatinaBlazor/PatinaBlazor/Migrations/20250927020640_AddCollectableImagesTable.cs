using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectableImagesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectableImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectableId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    IsMainImage = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectableImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectableImages_Collectables_CollectableId",
                        column: x => x.CollectableId,
                        principalTable: "Collectables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectableImages_CollectableId",
                table: "CollectableImages",
                column: "CollectableId");

            // Migrate existing image data to new CollectableImages table
            migrationBuilder.Sql(@"
                INSERT INTO CollectableImages (CollectableId, FileName, ContentType, FileSize, IsMainImage, DisplayOrder, CreatedDate)
                SELECT
                    Id as CollectableId,
                    ImageFileName as FileName,
                    ImageContentType as ContentType,
                    COALESCE(ImageFileSize, 0) as FileSize,
                    1 as IsMainImage,
                    0 as DisplayOrder,
                    datetime('now') as CreatedDate
                FROM Collectables
                WHERE ImageFileName IS NOT NULL AND ImageFileName != ''
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectableImages");
        }
    }
}
