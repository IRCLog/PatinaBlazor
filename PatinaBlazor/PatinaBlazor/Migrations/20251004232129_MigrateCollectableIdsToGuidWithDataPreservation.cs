using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class MigrateCollectableIdsToGuidWithDataPreservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Create temporary tables to backup existing data
            migrationBuilder.Sql(@"
                -- Create temp table for Collectables with mapping of old ID to new GUID
                CREATE TABLE #CollectablesTemp (
                    OldId int,
                    NewId uniqueidentifier,
                    Description nvarchar(500),
                    PricePaid decimal(18,2),
                    AskingPrice decimal(18,2),
                    DateAcquired datetime2,
                    AcquiredFrom nvarchar(200),
                    IsForSale bit,
                    IsSold bit,
                    DateSold datetime2,
                    SoldTo nvarchar(200),
                    SalePrice decimal(18,2),
                    Notes nvarchar(1000),
                    CreatedDate datetime2,
                    UserId nvarchar(128)
                );

                -- Create temp table for CollectableImages
                CREATE TABLE #CollectableImagesTemp (
                    Id int,
                    OldCollectableId int,
                    FileName nvarchar(500),
                    ContentType nvarchar(100),
                    FileSize bigint,
                    IsMainImage bit,
                    DisplayOrder int,
                    CreatedDate datetime2
                );
            ");

            // Step 2: Copy existing data to temp tables with new GUIDs
            migrationBuilder.Sql(@"
                -- Copy Collectables data with new GUIDs
                INSERT INTO #CollectablesTemp (OldId, NewId, Description, PricePaid, AskingPrice, DateAcquired, AcquiredFrom, IsForSale, IsSold, DateSold, SoldTo, SalePrice, Notes, CreatedDate, UserId)
                SELECT Id, NEWID(), Description, PricePaid, AskingPrice, DateAcquired, AcquiredFrom, IsForSale, IsSold, DateSold, SoldTo, SalePrice, Notes, CreatedDate, UserId
                FROM Collectables;

                -- Copy CollectableImages data
                INSERT INTO #CollectableImagesTemp (Id, OldCollectableId, FileName, ContentType, FileSize, IsMainImage, DisplayOrder, CreatedDate)
                SELECT Id, CollectableId, FileName, ContentType, FileSize, IsMainImage, DisplayOrder, CreatedDate
                FROM CollectableImages;
            ");

            // Step 3: Drop foreign key constraints and existing tables
            migrationBuilder.DropForeignKey(
                name: "FK_CollectableImages_Collectables_CollectableId",
                table: "CollectableImages");

            migrationBuilder.DropTable(name: "CollectableImages");
            migrationBuilder.DropTable(name: "Collectables");

            // Step 4: Create new tables with GUID IDs
            migrationBuilder.CreateTable(
                name: "Collectables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PricePaid = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AskingPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DateAcquired = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcquiredFrom = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsForSale = table.Column<bool>(type: "bit", nullable: false),
                    IsSold = table.Column<bool>(type: "bit", nullable: false),
                    DateSold = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SoldTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collectables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collectables_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectableImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CollectableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IsMainImage = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
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

            // Step 5: Restore data from temp tables
            migrationBuilder.Sql(@"
                -- Restore Collectables data
                INSERT INTO Collectables (Id, Description, PricePaid, AskingPrice, DateAcquired, AcquiredFrom, IsForSale, IsSold, DateSold, SoldTo, SalePrice, Notes, CreatedDate, UserId)
                SELECT NewId, Description, PricePaid, AskingPrice, DateAcquired, AcquiredFrom, IsForSale, IsSold, DateSold, SoldTo, SalePrice, Notes, CreatedDate, UserId
                FROM #CollectablesTemp;

                -- Restore CollectableImages data with new GUID foreign keys
                INSERT INTO CollectableImages (CollectableId, FileName, ContentType, FileSize, IsMainImage, DisplayOrder, CreatedDate)
                SELECT ct.NewId, cit.FileName, cit.ContentType, cit.FileSize, cit.IsMainImage, cit.DisplayOrder, cit.CreatedDate
                FROM #CollectableImagesTemp cit
                INNER JOIN #CollectablesTemp ct ON cit.OldCollectableId = ct.OldId;

                -- Clean up temp tables
                DROP TABLE #CollectableImagesTemp;
                DROP TABLE #CollectablesTemp;
            ");

            // Step 6: Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_CollectableImages_CollectableId",
                table: "CollectableImages",
                column: "CollectableId");

            migrationBuilder.CreateIndex(
                name: "IX_Collectables_UserId",
                table: "Collectables",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Rollback is not fully supported for this migration
            // as we cannot reliably convert GUIDs back to the original int IDs
            // This would require manual data recovery from backups
            throw new NotSupportedException(
                "Rollback of GUID to int ID migration is not supported. " +
                "The original int IDs cannot be recovered from GUIDs. " +
                "Please restore from a database backup if rollback is needed.");
        }
    }
}
