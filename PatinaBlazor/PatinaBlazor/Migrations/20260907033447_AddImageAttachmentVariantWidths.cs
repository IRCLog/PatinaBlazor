using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddImageAttachmentVariantWidths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediumWidth",
                table: "ImageAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThumbnailWidth",
                table: "ImageAttachments",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediumWidth",
                table: "ImageAttachments");

            migrationBuilder.DropColumn(
                name: "ThumbnailWidth",
                table: "ImageAttachments");
        }
    }
}
