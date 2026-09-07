using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Uses IDbContextFactory rather than an injected scoped ApplicationDbContext: Blazor
    // Server keeps one DI scope (and one scoped DbContext) alive for a circuit's entire
    // lifetime, not per page, so a query from the page a user just left can still be
    // in-flight when the next page's query starts - and EF Core's DbContext isn't safe
    // for concurrent use. Every method here gets its own short-lived context instead.
    public class ArticleService : IArticleService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public ArticleService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        // Admin/authoring

        public async Task<List<Article>> GetAllAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .OrderByDescending(a => a.CreatedDate)
                .ToListAsync();
        }

        public async Task<Article?> GetByIdAsync(Guid id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Articles
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

            await using var context = await _contextFactory.CreateDbContextAsync();
            context.Articles.Add(article);
            await context.SaveChangesAsync();
            return article;
        }

        public async Task UpdateAsync(Article article, string currentUserId)
        {
            NormalizePublishedDate(article);
            article.ModifiedDate = DateTime.UtcNow;
            article.ModifiedByUserId = currentUserId;

            await using var context = await _contextFactory.CreateDbContextAsync();
            context.Articles.Update(article);
            await context.SaveChangesAsync();
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
            await using var context = await _contextFactory.CreateDbContextAsync();
            var article = await context.Articles.FindAsync(id);
            if (article != null)
            {
                context.Articles.Remove(article);
                await context.SaveChangesAsync();
            }
        }

        public async Task PublishAsync(Guid id, string currentUserId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var article = await context.Articles.FindAsync(id);
            if (article == null) return;

            article.Status = ArticleStatus.Published;
            article.PublishedDate ??= DateTime.UtcNow;
            article.ModifiedDate = DateTime.UtcNow;
            article.ModifiedByUserId = currentUserId;
            await context.SaveChangesAsync();
        }

        public async Task UnpublishAsync(Guid id, string currentUserId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var article = await context.Articles.FindAsync(id);
            if (article == null) return;

            article.Status = ArticleStatus.Draft;
            article.ModifiedDate = DateTime.UtcNow;
            article.ModifiedByUserId = currentUserId;
            await context.SaveChangesAsync();
        }

        public async Task<ImageAttachment> AddArticleImageAsync(Guid articleId, ImageUploadResult upload, bool isMainImage)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var image = new ImageAttachment
            {
                ArticleId = articleId,
                FileName = upload.FileName,
                RelativePath = upload.RelativePath,
                ThumbnailRelativePath = upload.ThumbnailRelativePath,
                MediumRelativePath = upload.MediumRelativePath,
                ThumbnailWidth = upload.ThumbnailWidth,
                MediumWidth = upload.MediumWidth,
                ContentType = upload.ContentType,
                FileSize = upload.FileSize,
                IsMainImage = isMainImage,
                DisplayOrder = await context.ImageAttachments.CountAsync(i => i.ArticleId == articleId),
                CreatedDate = DateTime.UtcNow
            };

            context.ImageAttachments.Add(image);
            await context.SaveChangesAsync();
            return image;
        }

        public async Task<ImageAttachment?> GetArticleImageAsync(int imageId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ImageAttachments.FindAsync(imageId);
        }

        public async Task DeleteArticleImageAsync(int imageId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var image = await context.ImageAttachments.FindAsync(imageId);
            if (image != null)
            {
                context.ImageAttachments.Remove(image);
                await context.SaveChangesAsync();
            }
        }

        // Public/audience-filtered reads

        public async Task<List<Article>> GetVisibleArticlesAsync(IReadOnlyCollection<ArticleAudience> allowedAudiences)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Articles
                .AsNoTracking()
                .Include(a => a.Author)
                .Include(a => a.Images)
                .Where(a => a.Status == ArticleStatus.Published && allowedAudiences.Contains(a.Audience))
                .OrderByDescending(a => a.PublishedDate)
                .ToListAsync();
        }

        public async Task<Article?> GetPublishedArticleAsync(Guid id, IReadOnlyCollection<ArticleAudience> allowedAudiences)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Articles
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
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Articles
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
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Articles
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
