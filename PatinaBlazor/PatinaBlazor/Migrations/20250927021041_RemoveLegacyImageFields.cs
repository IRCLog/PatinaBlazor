using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyImageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageContentType",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "ImageFileName",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "ImageFileSize",
                table: "Collectables");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageContentType",
                table: "Collectables",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageFileName",
                table: "Collectables",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ImageFileSize",
                table: "Collectables",
                type: "INTEGER",
                nullable: true);
        }
    }
}
