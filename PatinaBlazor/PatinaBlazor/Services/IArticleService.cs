using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public interface IArticleService
    {
        // Admin/authoring
        Task<List<Article>> GetAllAsync();
        Task<Article?> GetByIdAsync(Guid id);
        Task<Article> CreateAsync(Article article, string currentUserId);
        Task UpdateAsync(Article article, string currentUserId);
        Task DeleteAsync(Guid id);
        Task PublishAsync(Guid id, string currentUserId);
        Task UnpublishAsync(Guid id, string currentUserId);

        Task<ImageAttachment> AddArticleImageAsync(Guid articleId, ImageUploadResult upload, bool isMainImage);
        Task<ImageAttachment?> GetArticleImageAsync(int imageId);
        Task DeleteArticleImageAsync(int imageId);

        // Public/audience-filtered reads
        Task<List<Article>> GetVisibleArticlesAsync(IReadOnlyCollection<ArticleAudience> allowedAudiences);
        Task<Article?> GetPublishedArticleAsync(Guid id, IReadOnlyCollection<ArticleAudience> allowedAudiences);
        Task<List<Article>> GetFeaturedForHomeAsync(int count = 3);
        Task<List<Article>> GetFeaturedForStorageLandingAsync(int count = 3);
    }
}
