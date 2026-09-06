using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class ArticleService : IArticleService
    {
        private readonly ApplicationDbContext _context;

        public ArticleService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Admin/authoring

        public async Task<List<Article>> GetAllAsync()
        {
            return await _context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .OrderByDescending(a => a.CreatedDate)
                .ToListAsync();
        }

        public async Task<Article?> GetByIdAsync(Guid id)
        {
            return await _context.Articles
                .Include(a => a.Author)
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<Article> CreateAsync(Article article, string currentUserId)
        {
            NormalizePublishedDate(article);
            article.CreatedDate = DateTime.UtcNow;
            article.ModifiedDate = DateTime.UtcNow;
            article.CreatedByUserId = currentUserId;
            article.ModifiedByUserId = currentUserId;

            _context.Articles.Add(article);
            await _context.SaveChangesAsync();
            return article;
        }

        public async Task UpdateAsync(Article article, string currentUserId)
        {
            NormalizePublishedDate(article);
            article.ModifiedDate = DateTime.UtcNow;
            article.ModifiedByUserId = currentUserId;
            _context.Articles.Update(article);
            await _context.SaveChangesAsync();
        }

        // PublishedDate is set once, the first time Status becomes Published, regardless
        // of whether that happens via the authoring dialog's Status field or the dedicated
        // PublishAsync action - and is preserved across later edits/unpublish cycles.
        private static void NormalizePublishedDate(Article article)
        {
            if (article.Status == ArticleStatus.Published && article.PublishedDate == null)
            {
                article.PublishedDate = DateTime.UtcNow;
            }
        }

        public async Task DeleteAsync(Guid id)
        {
            var article = await _context.Articles.FindAsync(id);
            if (article != null)
            {
                _context.Articles.Remove(article);
                await _context.SaveChangesAsync();
            }
        }

        public async Task PublishAsync(Guid id, string currentUserId)
        {
            var article = await _context.Articles.FindAsync(id);
            if (article == null) return;

            article.Status = ArticleStatus.Published;
            article.PublishedDate ??= DateTime.UtcNow;
            article.ModifiedDate = DateTime.UtcNow;
            article.ModifiedByUserId = currentUserId;
            await _context.SaveChangesAsync();
        }

        public async Task UnpublishAsync(Guid id, string currentUserId)
        {
            var article = await _context.Articles.FindAsync(id);
            if (article == null) return;

            article.Status = ArticleStatus.Draft;
            article.ModifiedDate = DateTime.UtcNow;
            article.ModifiedByUserId = currentUserId;
            await _context.SaveChangesAsync();
        }

        public async Task<ImageAttachment> AddArticleImageAsync(Guid articleId, ImageUploadResult upload, bool isMainImage)
        {
            var image = new ImageAttachment
            {
                ArticleId = articleId,
                FileName = upload.FileName,
                RelativePath = upload.RelativePath,
                ThumbnailRelativePath = upload.ThumbnailRelativePath,
                MediumRelativePath = upload.MediumRelativePath,
                ContentType = upload.ContentType,
                FileSize = upload.FileSize,
                IsMainImage = isMainImage,
                DisplayOrder = await _context.ImageAttachments.CountAsync(i => i.ArticleId == articleId),
                CreatedDate = DateTime.UtcNow
            };

            _context.ImageAttachments.Add(image);
            await _context.SaveChangesAsync();
            return image;
        }

        public async Task<ImageAttachment?> GetArticleImageAsync(int imageId)
        {
            return await _context.ImageAttachments.FindAsync(imageId);
        }

        public async Task DeleteArticleImageAsync(int imageId)
        {
            var image = await _context.ImageAttachments.FindAsync(imageId);
            if (image != null)
            {
                _context.ImageAttachments.Remove(image);
                await _context.SaveChangesAsync();
            }
        }

        // Public/audience-filtered reads

        public async Task<List<Article>> GetVisibleArticlesAsync(IReadOnlyCollection<ArticleAudience> allowedAudiences)
        {
            return await _context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .Where(a => a.Status == ArticleStatus.Published && allowedAudiences.Contains(a.Audience))
                .OrderByDescending(a => a.PublishedDate)
                .ToListAsync();
        }

        public async Task<Article?> GetPublishedArticleAsync(Guid id, IReadOnlyCollection<ArticleAudience> allowedAudiences)
        {
            return await _context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.Status == ArticleStatus.Published &&
                    allowedAudiences.Contains(a.Audience));
        }

        public async Task<List<Article>> GetFeaturedForHomeAsync(int count = 3)
        {
            return await _context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .Where(a => a.Status == ArticleStatus.Published
                            && a.FeatureOnHomePage
                            && a.Audience == ArticleAudience.Public)
                .OrderByDescending(a => a.PublishedDate)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<Article>> GetFeaturedForStorageLandingAsync(int count = 3)
        {
            return await _context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .Where(a => a.Status == ArticleStatus.Published && a.FeatureOnStorageLanding)
                .OrderByDescending(a => a.PublishedDate)
                .Take(count)
                .ToListAsync();
        }
    }
}
