using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemCollectionAndAllCollectables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add IsSystemCollection column
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemCollection",
                table: "CollectableCollections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Create 'All Collectables' collection for each existing user
            migrationBuilder.Sql(@"
                INSERT INTO CollectableCollections (Id, Name, CreatedDate, ModifiedDate, UserId, IsSystemCollection)
                SELECT
                    NEWID(),
                    'All Collectables',
                    GETUTCDATE(),
                    GETUTCDATE(),
                    Id,
                    1
                FROM AspNetUsers
                WHERE NOT EXISTS (
                    SELECT 1 FROM CollectableCollections
                    WHERE CollectableCollections.UserId = AspNetUsers.Id
                    AND CollectableCollections.Name = 'All Collectables'
                )
            ");

            // Add all existing collectables to their user's 'All Collectables' collection
            migrationBuilder.Sql(@"
                INSERT INTO CollectableCollectionItems (Id, CollectableCollectionId, CollectableId, AddedDate)
                SELECT
                    NEWID(),
                    cc.Id,
                    c.Id,
                    GETUTCDATE()
                FROM Collectables c
                INNER JOIN CollectableCollections cc ON cc.UserId = c.UserId AND cc.IsSystemCollection = 1
                WHERE NOT EXISTS (
                    SELECT 1 FROM CollectableCollectionItems cci
                    WHERE cci.CollectableCollectionId = cc.Id AND cci.CollectableId = c.Id
                )
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystemCollection",
                table: "CollectableCollections");
        }
    }
}
