using Microsoft.AspNetCore.Components.Forms;

namespace PatinaBlazor.Services
{
    public interface IImageService
    {
        Task<ImageUploadResult> SaveImageAsync(IBrowserFile file, string subfolder = "collectables");
        Task<bool> DeleteImageAsync(string fileName, string subfolder = "collectables");
        string GetImageUrl(string fileName, string subfolder = "collectables");
        bool IsValidImageFile(IBrowserFile file);
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

        public async Task<bool> DeleteImageAsync(string fileName, string subfolder = "collectables")
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return true;

                var filePath = Path.Combine(_environment.WebRootPath, "uploads", subfolder, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Image deleted successfully: {FileName}", fileName);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image file: {FileName}", fileName);
                return false;
            }
        }

        public string GetImageUrl(string fileName, string subfolder = "collectables")
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            return $"/uploads/{subfolder}/{fileName}";
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