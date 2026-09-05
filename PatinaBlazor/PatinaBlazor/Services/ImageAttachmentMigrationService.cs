using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // One-time startup routine that converts rows in the pre-unification CollectableImages/
    // StoragePropertyImages tables into the unified ImageAttachments table, re-running each
    // image's physical file through IImageService so it gets the same large/medium/thumb
    // variants a fresh upload would. Runs once per legacy table: converts every row, then drops
    // that table via raw SQL only if every row converted successfully. Safe to run on every
    // startup indefinitely - once a legacy table no longer exists, its migration is a no-op, so
    // this never needs to be manually disabled or removed after the one real run in production.
    public class ImageAttachmentMigrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IImageService _imageService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageAttachmentMigrationService> _logger;

        public ImageAttachmentMigrationService(
            ApplicationDbContext context,
            IImageService imageService,
            IWebHostEnvironment environment,
            ILogger<ImageAttachmentMigrationService> logger)
        {
            _context = context;
            _imageService = imageService;
            _environment = environment;
            _logger = logger;
        }

        public async Task MigrateLegacyImagesAsync()
        {
            await MigrateTableAsync(
                tableName: "CollectableImages",
                ownerIdColumn: "CollectableId",
                subfolder: Collectable.ImageSubfolderName,
                readOwnerId: reader => reader.GetGuid(reader.GetOrdinal("CollectableId")),
                assignOwner: (image, ownerId) => image.CollectableId = (Guid)ownerId);

            await MigrateTableAsync(
                tableName: "StoragePropertyImages",
                ownerIdColumn: "StoragePropertyId",
                subfolder: StorageProperty.ImageSubfolderName,
                readOwnerId: reader => reader.GetInt32(reader.GetOrdinal("StoragePropertyId")),
                assignOwner: (image, ownerId) => image.StoragePropertyId = (int)ownerId);
        }

        private async Task MigrateTableAsync(
            string tableName,
            string ownerIdColumn,
            string subfolder,
            Func<System.Data.Common.DbDataReader, object> readOwnerId,
            Action<ImageAttachment, object> assignOwner)
        {
            var connection = _context.Database.GetDbConnection();
            var wasClosed = connection.State != System.Data.ConnectionState.Open;
            if (wasClosed)
            {
                await connection.OpenAsync();
            }

            try
            {
                if (!await TableExistsAsync(connection, tableName))
                {
                    return; // Already migrated and dropped in a previous run, or never existed.
                }

                var legacyRows = new List<LegacyImageRow>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"SELECT Id, {ownerIdColumn}, FileName, IsMainImage, DisplayOrder, CreatedDate FROM [{tableName}]";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        legacyRows.Add(new LegacyImageRow
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            OwnerId = readOwnerId(reader),
                            FileName = reader.GetString(reader.GetOrdinal("FileName")),
                            IsMainImage = reader.GetBoolean(reader.GetOrdinal("IsMainImage")),
                            DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                            CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate"))
                        });
                    }
                }

                if (legacyRows.Count == 0)
                {
                    _logger.LogInformation("{TableName} has no rows to migrate; dropping the empty legacy table.", tableName);
                    await DropTableAsync(connection, tableName);
                    return;
                }

                _logger.LogInformation("Migrating {Count} legacy image(s) from {TableName} into ImageAttachments.", legacyRows.Count, tableName);

                var failures = 0;
                foreach (var row in legacyRows)
                {
                    var sourcePath = Path.Combine(_environment.WebRootPath, "uploads", subfolder, row.FileName);
                    var upload = await _imageService.SaveImageFromDiskAsync(sourcePath, subfolder);

                    if (!upload.Success)
                    {
                        failures++;
                        _logger.LogError("Failed to migrate legacy image {TableName}.Id={Id} ({FileName}): {Error}", tableName, row.Id, row.FileName, upload.ErrorMessage);
                        continue;
                    }

                    var attachment = new ImageAttachment
                    {
                        FileName = upload.FileName,
                        RelativePath = upload.RelativePath,
                        ThumbnailRelativePath = upload.ThumbnailRelativePath,
                        MediumRelativePath = upload.MediumRelativePath,
                        ContentType = upload.ContentType,
                        FileSize = upload.FileSize,
                        IsMainImage = row.IsMainImage,
                        DisplayOrder = row.DisplayOrder,
                        CreatedDate = row.CreatedDate // preserve original history, not DateTime.UtcNow
                    };
                    assignOwner(attachment, row.OwnerId);

                    _context.ImageAttachments.Add(attachment);
                }

                await _context.SaveChangesAsync();

                if (failures > 0)
                {
                    _logger.LogWarning("{Failures} of {Count} legacy image(s) in {TableName} failed to migrate; leaving the table in place so this can be retried on the next startup.", failures, legacyRows.Count, tableName);
                    return;
                }

                _logger.LogInformation("Successfully migrated all {Count} legacy image(s) from {TableName}; dropping the legacy table.", legacyRows.Count, tableName);
                await DropTableAsync(connection, tableName);
            }
            finally
            {
                if (wasClosed)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection connection, string tableName)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName) THEN 1 ELSE 0 END";
            var param = cmd.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = tableName;
            cmd.Parameters.Add(param);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) == 1;
        }

        private static async Task DropTableAsync(System.Data.Common.DbConnection connection, string tableName)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DROP TABLE [{tableName}]";
            await cmd.ExecuteNonQueryAsync();
        }

        private class LegacyImageRow
        {
            public int Id { get; set; }
            public object OwnerId { get; set; } = null!;
            public string FileName { get; set; } = string.Empty;
            public bool IsMainImage { get; set; }
            public int DisplayOrder { get; set; }
            public DateTime CreatedDate { get; set; }
        }
    }
}
