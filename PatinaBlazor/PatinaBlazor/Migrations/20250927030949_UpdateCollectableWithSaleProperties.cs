using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCollectableWithSaleProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Price",
                table: "Collectables",
                newName: "PricePaid");

            migrationBuilder.AddColumn<string>(
                name: "AcquiredFrom",
                table: "Collectables",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AskingPrice",
                table: "Collectables",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAcquired",
                table: "Collectables",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateSold",
                table: "Collectables",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsForSale",
                table: "Collectables",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSold",
                table: "Collectables",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice",
                table: "Collectables",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoldTo",
                table: "Collectables",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquiredFrom",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "AskingPrice",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "DateAcquired",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "DateSold",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "IsForSale",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "IsSold",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "SalePrice",
                table: "Collectables");

            migrationBuilder.DropColumn(
                name: "SoldTo",
                table: "Collectables");

            migrationBuilder.RenameColumn(
                name: "PricePaid",
                table: "Collectables",
                newName: "Price");
        }
    }
}
