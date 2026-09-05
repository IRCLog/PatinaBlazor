using Microsoft.AspNetCore.Components.Forms;
using PatinaBlazor.Data;
using SkiaSharp;

namespace PatinaBlazor.Services
{
    public interface IImageService
    {
        Task<ImageUploadResult> SaveImageAsync(IBrowserFile file, string subfolder);
        Task<List<ImageUploadResult>> SaveMultipleImagesAsync(IReadOnlyList<IBrowserFile> files, string subfolder);
        Task<ImageUploadResult> SaveImageFromDiskAsync(string sourceFilePath, string subfolder);
        Task<bool> DeleteImageAsync(ImageAttachment image);
        Task<bool> DeleteImageAsync(ImageUploadResult uploadResult);
        bool IsValidImageFile(IBrowserFile file);
        List<string> ValidateImageFiles(IReadOnlyList<IBrowserFile> files);
    }

    public class ImageService : IImageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageService> _logger;
        private readonly long _maxFileSize = 15 * 1024 * 1024; // 15MB
        private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        // Every uploaded image gets these three JPEG variants, sharing one GUID base name.
        private const int LargeMaxDimension = 2048;
        private const int MediumMaxDimension = 1000;
        private const int ThumbnailMaxDimension = 400;

        public ImageService(IWebHostEnvironment environment, ILogger<ImageService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public bool IsValidImageFile(IBrowserFile file)
        {
            if (file == null) return false;

            // Check file size
            if (file.Size > _maxFileSize) return false;

            // Check content type
            if (!file.ContentType.StartsWith("image/")) return false;

            // Check file extension
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            return _allowedExtensions.Contains(extension);
        }

        public async Task<ImageUploadResult> SaveImageAsync(IBrowserFile file, string subfolder)
        {
            if (!IsValidImageFile(file))
            {
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = "Invalid file. Please upload a valid image file (max 15MB)."
                };
            }

            try
            {
                return await SaveFromStreamAsync(() => file.OpenReadStream(_maxFileSize), file.Name, file.ContentType, file.Size, subfolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving image file: {FileName}", file.Name);
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = "An error occurred while uploading the image."
                };
            }
        }

        public async Task<List<ImageUploadResult>> SaveMultipleImagesAsync(IReadOnlyList<IBrowserFile> files, string subfolder)
        {
            var results = new List<ImageUploadResult>();

            foreach (var file in files)
            {
                var result = await SaveImageAsync(file, subfolder);
                results.Add(result);

                // If one fails, stop processing to avoid partial uploads
                if (!result.Success)
                {
                    break;
                }
            }

            return results;
        }

        // Used by ImageAttachmentMigrationService to re-run an already-on-disk legacy image
        // (from the pre-unification CollectableImages/StoragePropertyImages tables) through the
        // exact same decode/resize/encode pipeline a fresh browser upload gets, so every image
        // in the app - old or new - ends up with the same large/medium/thumb variants.
        public async Task<ImageUploadResult> SaveImageFromDiskAsync(string sourceFilePath, string subfolder)
        {
            try
            {
                if (!File.Exists(sourceFilePath))
                {
                    return new ImageUploadResult { Success = false, ErrorMessage = $"Source file not found: {sourceFilePath}" };
                }

                var fileInfo = new FileInfo(sourceFilePath);
                return await SaveFromStreamAsync(() => File.OpenRead(sourceFilePath), fileInfo.Name, contentType: string.Empty, fileInfo.Length, subfolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating image file from disk: {SourceFilePath}", sourceFilePath);
                return new ImageUploadResult { Success = false, ErrorMessage = "An error occurred while migrating the image." };
            }
        }

        // Decodes once and derives thumbnail/medium/large JPEG variants from the same bitmap,
        // sharing one GUID base name (_thumb/_medium suffixes, no suffix = large/primary).
        // Everything is normalized to JPEG regardless of the source format, since the variants
        // are re-encoded anyway. Shared by both a fresh browser upload and a from-disk migration
        // of a pre-existing legacy image.
        private async Task<ImageUploadResult> SaveFromStreamAsync(Func<Stream> openStream, string originalFileName, string contentType, long originalFileSize, string subfolder)
        {
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", subfolder);
            Directory.CreateDirectory(uploadsPath);

            var baseId = Guid.NewGuid().ToString();

            using (var inputStream = openStream())
            using (var skData = SKData.Create(inputStream))
            using (var skBitmap = SKBitmap.Decode(skData))
            {
                if (skBitmap != null)
                {
                    var fileName = $"{baseId}.jpg";
                    var mediumFileName = $"{baseId}_medium.jpg";
                    var thumbFileName = $"{baseId}_thumb.jpg";
                    var filePath = Path.Combine(uploadsPath, fileName);

                    ResizeAndEncodeJpeg(skBitmap, LargeMaxDimension, filePath);
                    ResizeAndEncodeJpeg(skBitmap, MediumMaxDimension, Path.Combine(uploadsPath, mediumFileName));
                    ResizeAndEncodeJpeg(skBitmap, ThumbnailMaxDimension, Path.Combine(uploadsPath, thumbFileName));

                    _logger.LogInformation("Image saved with responsive sizes: {FileName}", fileName);

                    return new ImageUploadResult
                    {
                        Success = true,
                        FileName = fileName,
                        RelativePath = $"/uploads/{subfolder}/{fileName}",
                        ThumbnailRelativePath = $"/uploads/{subfolder}/{thumbFileName}",
                        MediumRelativePath = $"/uploads/{subfolder}/{mediumFileName}",
                        ContentType = "image/jpeg",
                        FileSize = new FileInfo(filePath).Length
                    };
                }
            }

            // Not decodable as an image despite passing validation - fall back to a plain copy
            // under the original extension (no thumbnail/medium variants) rather than failing.
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var fallbackFileName = $"{baseId}{extension}";
            var fallbackPath = Path.Combine(uploadsPath, fallbackFileName);
            using (var freshStream = openStream())
            using (var outStream = File.Create(fallbackPath))
            {
                await freshStream.CopyToAsync(outStream);
            }

            _logger.LogWarning("Could not decode image for responsive sizing, saved as-is: {FileName}", fallbackFileName);

            return new ImageUploadResult
            {
                Success = true,
                FileName = fallbackFileName,
                RelativePath = $"/uploads/{subfolder}/{fallbackFileName}",
                ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
                FileSize = originalFileSize
            };
        }

        private static void ResizeAndEncodeJpeg(SKBitmap sourceBitmap, int maxDimension, string outputPath, int quality = 82)
        {
            var scale = Math.Min((float)maxDimension / sourceBitmap.Width, (float)maxDimension / sourceBitmap.Height);
            scale = Math.Min(scale, 1.0f); // never upscale

            var newWidth = Math.Max(1, (int)(sourceBitmap.Width * scale));
            var newHeight = Math.Max(1, (int)(sourceBitmap.Height * scale));

            using var resizedBitmap = sourceBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
            using var image = SKImage.FromBitmap(resizedBitmap ?? sourceBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            using var outputStream = File.Create(outputPath);
            data.SaveTo(outputStream);
        }

        public Task<bool> DeleteImageAsync(ImageAttachment image)
        {
            return DeletePathsAsync(image.RelativePath, image.ThumbnailRelativePath, image.MediumRelativePath, image.FileName);
        }

        public Task<bool> DeleteImageAsync(ImageUploadResult uploadResult)
        {
            return DeletePathsAsync(uploadResult.RelativePath, uploadResult.ThumbnailRelativePath, uploadResult.MediumRelativePath, uploadResult.FileName);
        }

        private Task<bool> DeletePathsAsync(string relativePath, string? thumbnailRelativePath, string? mediumRelativePath, string fileNameForLogging)
        {
            try
            {
                DeleteFileIfExists(ResolvePhysicalPath(relativePath));
                if (!string.IsNullOrEmpty(thumbnailRelativePath)) DeleteFileIfExists(ResolvePhysicalPath(thumbnailRelativePath));
                if (!string.IsNullOrEmpty(mediumRelativePath)) DeleteFileIfExists(ResolvePhysicalPath(mediumRelativePath));

                _logger.LogInformation("Image deleted successfully: {FileName}", fileNameForLogging);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image file: {FileName}", fileNameForLogging);
                return Task.FromResult(false);
            }
        }

        private string ResolvePhysicalPath(string relativePath)
        {
            var trimmed = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_environment.WebRootPath, trimmed);
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public List<string> ValidateImageFiles(IReadOnlyList<IBrowserFile> files)
        {
            var errors = new List<string>();

            if (files == null || !files.Any())
            {
                errors.Add("No files selected.");
                return errors;
            }

            if (files.Count > 10) // Limit to 10 images per upload
            {
                errors.Add("Maximum 10 images can be uploaded at once.");
            }

            foreach (var file in files)
            {
                if (!IsValidImageFile(file))
                {
                    errors.Add($"Invalid file: {file.Name}. Please select valid image files (max 15MB each).");
                }
            }

            return errors;
        }
    }

    public class ImageUploadResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? ThumbnailRelativePath { get; set; }
        public string? MediumRelativePath { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
