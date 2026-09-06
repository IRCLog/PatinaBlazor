using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ImageAttachment_ExactlyOneOwner",
                table: "ImageAttachments");

            migrationBuilder.AddColumn<Guid>(
                name: "ArticleId",
                table: "ImageAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArticleId",
                table: "HitCounters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TagLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Audience = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeatureOnHomePage = table.Column<bool>(type: "bit", nullable: false),
                    FeatureOnStorageLanding = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Articles_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Articles_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Articles_AspNetUsers_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageAttachments_ArticleId",
                table: "ImageAttachments",
                column: "ArticleId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImageAttachment_ExactlyOneOwner",
                table: "ImageAttachments",
                sql: "(CASE WHEN [CollectableId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [StoragePropertyId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [ArticleId] IS NOT NULL THEN 1 ELSE 0 END) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_HitCounters_ArticleId",
                table: "HitCounters",
                column: "ArticleId",
                unique: true,
                filter: "[ArticleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_AuthorUserId",
                table: "Articles",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_CreatedByUserId",
                table: "Articles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_ModifiedByUserId",
                table: "Articles",
                column: "ModifiedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_HitCounters_Articles_ArticleId",
                table: "HitCounters",
                column: "ArticleId",
                principalTable: "Articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ImageAttachments_Articles_ArticleId",
                table: "ImageAttachments",
                column: "ArticleId",
                principalTable: "Articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HitCounters_Articles_ArticleId",
                table: "HitCounters");

            migrationBuilder.DropForeignKey(
                name: "FK_ImageAttachments_Articles_ArticleId",
                table: "ImageAttachments");

            migrationBuilder.DropTable(
                name: "Articles");

            migrationBuilder.DropIndex(
                name: "IX_ImageAttachments_ArticleId",
                table: "ImageAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ImageAttachment_ExactlyOneOwner",
                table: "ImageAttachments");

            migrationBuilder.DropIndex(
                name: "IX_HitCounters_ArticleId",
                table: "HitCounters");

            migrationBuilder.DropColumn(
                name: "ArticleId",
                table: "ImageAttachments");

            migrationBuilder.DropColumn(
                name: "ArticleId",
                table: "HitCounters");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImageAttachment_ExactlyOneOwner",
                table: "ImageAttachments",
                sql: "([CollectableId] IS NOT NULL AND [StoragePropertyId] IS NULL) OR ([CollectableId] IS NULL AND [StoragePropertyId] IS NOT NULL)");
        }
    }
}
