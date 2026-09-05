using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class UnifyImageAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately does NOT drop CollectableImages/StoragePropertyImages here.
            // Real environments will have rows in those tables, and converting a row means
            // re-running its physical image file through ImageService (SkiaSharp resize/encode)
            // to produce the new large/medium/thumb variants - that requires C#, not SQL, so it
            // can't happen inside this migration. ImageAttachmentMigrationService performs that
            // conversion at app startup (see Program.cs) and drops both legacy tables itself via
            // raw SQL once every row has been converted successfully. See CLAUDE.md for the
            // full rationale. This migration only adds the new table.
            migrationBuilder.CreateTable(
                name: "ImageAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ThumbnailRelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MediumRelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IsMainImage = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CollectableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StoragePropertyId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageAttachments", x => x.Id);
                    table.CheckConstraint("CK_ImageAttachment_ExactlyOneOwner", "([CollectableId] IS NOT NULL AND [StoragePropertyId] IS NULL) OR ([CollectableId] IS NULL AND [StoragePropertyId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ImageAttachments_Collectables_CollectableId",
                        column: x => x.CollectableId,
                        principalTable: "Collectables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImageAttachments_StorageProperties_StoragePropertyId",
                        column: x => x.StoragePropertyId,
                        principalTable: "StorageProperties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageAttachments_CollectableId",
                table: "ImageAttachments",
                column: "CollectableId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAttachments_StoragePropertyId",
                table: "ImageAttachments",
                column: "StoragePropertyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only reverses what Up() actually did. If ImageAttachmentMigrationService has
            // already dropped the legacy tables by the time anyone runs this Down(), they are
            // gone for good - this migration was never the one that dropped them, so it has
            // nothing to restore.
            migrationBuilder.DropTable(
                name: "ImageAttachments");
        }
    }
}
