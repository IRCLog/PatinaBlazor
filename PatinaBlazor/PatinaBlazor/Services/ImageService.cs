using Microsoft.AspNetCore.Components.Forms;
using PatinaBlazor.Data;

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
        private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB
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
                        ErrorMessage = "Invalid file. Please upload a valid image file (max 5MB)."
                    };
                }

                // Create unique filename
                var extension = Path.GetExtension(file.Name).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid()}{extension}";

                // Create directory path
                var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", subfolder);
                Directory.CreateDirectory(uploadsPath);

                // Save file
                var filePath = Path.Combine(uploadsPath, fileName);
                using var stream = File.Create(filePath);
                await file.OpenReadStream(_maxFileSize).CopyToAsync(stream);

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
                    errors.Add($"Invalid file: {file.Name}. Please select valid image files (max 5MB each).");
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