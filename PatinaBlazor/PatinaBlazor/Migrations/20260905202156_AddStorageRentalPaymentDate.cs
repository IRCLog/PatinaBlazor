using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageRentalPaymentDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable first so existing rows aren't rejected, backfill from StartDate
            // (the same value used for dummy-seeded rentals), then tighten to NOT NULL -
            // avoids a destructive/nonsensical default for rentals that predate this column.
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDate",
                table: "StorageRentals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("UPDATE StorageRentals SET PaymentDate = StartDate WHERE PaymentDate IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaymentDate",
                table: "StorageRentals",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentDate",
                table: "StorageRentals");
        }
    }
}
