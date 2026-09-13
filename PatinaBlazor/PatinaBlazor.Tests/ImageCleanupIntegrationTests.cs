using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Exercises EntityLogicUnitInterceptor -> ImageCleanupLogicUnit end to end against a real
    // SQL Server (via DatabaseFixture) and real files on disk - the exact scenario that was
    // manually re-verified by hand (throwaway harness + browser) after every change to the
    // interceptor across this project's history (see CLAUDE.md's EntityLogicUnit entries).
    [Collection("Database")]
    public class ImageCleanupIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public ImageCleanupIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeletingArticle_RemovesImageAttachmentRowAndPhysicalFiles()
        {
            using var scope = _fixture.CreateScope();
            var imageService = scope.ServiceProvider.GetRequiredService<IImageService>();
            var articleService = scope.ServiceProvider.GetRequiredService<IArticleService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var author = await userManager.FindByEmailAsync(DevTestAccounts.AutomationEmail);
            Assert.NotNull(author);

            var article = await articleService.CreateAsync(new Article
            {
                Title = "Integration Test Article",
                Body = "test body",
                AuthorUserId = author!.Id
            }, author.Id);

            var sourceImagePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "test-image.jpg");
            var upload = await imageService.SaveImageFromDiskAsync(sourceImagePath, Article.ImageSubfolderName);
            var imageAttachment = await articleService.AddArticleImageAsync(article.Id, upload, isMainImage: true);

            var thumbnailPath = Path.Combine(_fixture.WebRootPath, imageAttachment.ThumbnailRelativePath!.TrimStart('/'));
            var mediumPath = Path.Combine(_fixture.WebRootPath, imageAttachment.MediumRelativePath!.TrimStart('/'));
            var fullPath = Path.Combine(_fixture.WebRootPath, imageAttachment.RelativePath.TrimStart('/'));
            Assert.True(File.Exists(thumbnailPath));
            Assert.True(File.Exists(mediumPath));
            Assert.True(File.Exists(fullPath));

            await articleService.DeleteAsync(article.Id);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            Assert.Null(await context.Articles.FindAsync(article.Id));
            Assert.False(await context.ImageAttachments.AnyAsync(i => i.ArticleId == article.Id));
            Assert.False(File.Exists(thumbnailPath));
            Assert.False(File.Exists(mediumPath));
            Assert.False(File.Exists(fullPath));
        }
    }
}
