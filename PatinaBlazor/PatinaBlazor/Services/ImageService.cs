using Microsoft.AspNetCore.Components.Forms;
using PatinaBlazor.Data;
using SkiaSharp;

namespace PatinaBlazor.Services
{
    public interface IImageService
    {
        Task<ImageUploadResult> SaveImageAsync(IBrowserFile file, string subfolder = "collectables");
        Task<List<ImageUploadResult>> SaveMultipleImagesAsync(IReadOnlyList<IBrowserFile> files, string subfolder = "collectables");
        Task<bool> DeleteImageAsync(string fileName, string subfolder = "collectables");
        Task<bool> DeleteCollectableImageAsync(CollectableImage image);
        string GetImageUrl(string fileName, string subfolder = "collectables");
        string GetImageUrl(CollectableImage image);
        bool IsValidImageFile(IBrowserFile file);
        List<string> ValidateImageFiles(IReadOnlyList<IBrowserFile> files);
    }

    public class ImageService : IImageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageService> _logger;
        private readonly long _maxFileSize = 15 * 1024 * 1024; // 15MB
        private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

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

        public async Task<ImageUploadResult> SaveImageAsync(IBrowserFile file, string subfolder = "collectables")
        {
            try
            {
                if (!IsValidImageFile(file))
                {
                    return new ImageUploadResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid file. Please upload a valid image file (max 15MB)."
                    };
                }

                // Create unique filename
                var extension = Path.GetExtension(file.Name).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid()}{extension}";

                // Create directory path
                var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", subfolder);
                Directory.CreateDirectory(uploadsPath);

                // Save file with optional compression
                var filePath = Path.Combine(uploadsPath, fileName);
                await SaveImageWithCompressionAsync(file, filePath);

                _logger.LogInformation("Image saved successfully: {FileName}", fileName);

                return new ImageUploadResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = filePath,
                    ContentType = file.ContentType,
                    FileSize = file.Size
                };
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

        private async Task SaveImageWithCompressionAsync(IBrowserFile file, string filePath)
        {
            const long compressionThreshold = 3 * 1024 * 1024; // 3MB

            // If file is smaller than threshold, save directly
            if (file.Size <= compressionThreshold)
            {
                using var stream = File.Create(filePath);
                await file.OpenReadStream(_maxFileSize).CopyToAsync(stream);
                return;
            }

            // Compress the image
            try
            {
                using var inputStream = file.OpenReadStream(_maxFileSize);
                using var skData = SKData.Create(inputStream);
                using var skBitmap = SKBitmap.Decode(skData);

                if (skBitmap == null)
                {
                    // If we can't decode as image, save as-is
                    using var stream = File.Create(filePath);
                    inputStream.Position = 0;
                    await inputStream.CopyToAsync(stream);
                    return;
                }

                // Calculate new dimensions (max 2048px on either side)
                var maxDimension = 2048;
                var scale = Math.Min((float)maxDimension / skBitmap.Width, (float)maxDimension / skBitmap.Height);
                scale = Math.Min(scale, 1.0f); // Don't upscale

                var newWidth = (int)(skBitmap.Width * scale);
                var newHeight = (int)(skBitmap.Height * scale);

                // Resize and compress
                using var resizedBitmap = skBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
                using var image = SKImage.FromBitmap(resizedBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80); // 80% quality

                // Save compressed image
                using var outputStream = File.Create(filePath);
                data.SaveTo(outputStream);

                _logger.LogInformation("Image compressed from {OriginalSize} bytes to {CompressedSize} bytes",
                    file.Size, data.Size);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compress image {FileName}, saving original", file.Name);
                // If compression fails, save original
                using var stream = File.Create(filePath);
                using var inputStream = file.OpenReadStream(_maxFileSize);
                await inputStream.CopyToAsync(stream);
            }
        }

        public Task<bool> DeleteImageAsync(string fileName, string subfolder = "collectables")
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return Task.FromResult(true);

                var filePath = Path.Combine(_environment.WebRootPath, "uploads", subfolder, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Image deleted successfully: {FileName}", fileName);
                }
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image file: {FileName}", fileName);
                return Task.FromResult(false);
            }
        }

        public async Task<bool> DeleteCollectableImageAsync(CollectableImage image)
        {
            return await DeleteImageAsync(image.FileName);
        }

        public string GetImageUrl(string fileName, string subfolder = "collectables")
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            return $"/uploads/{subfolder}/{fileName}";
        }

        public string GetImageUrl(CollectableImage image)
        {
            return GetImageUrl(image.FileName);
        }

        public async Task<List<ImageUploadResult>> SaveMultipleImagesAsync(IReadOnlyList<IBrowserFile> files, string subfolder = "collectables")
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
        public string FilePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}