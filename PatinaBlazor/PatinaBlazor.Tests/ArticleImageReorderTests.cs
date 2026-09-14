using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Reproduces the ReorderArticleImagesAsync verification that was previously done once via
    // a throwaway console harness, then deleted (see CLAUDE.md's 2026-09-08 media-layout-
    // templates entry) - DisplayOrder is the slot index and "first in order" must always stay
    // in sync with IsMainImage, since GetMainImage() and every card-thumbnail spot in the app
    // rely on exactly one image having IsMainImage = true.
    [Collection("Database")]
    public class ArticleImageReorderTests
    {
        private readonly DatabaseFixture _fixture;

        public ArticleImageReorderTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ReorderingImages_UpdatesDisplayOrderAndKeepsExactlyOneMainImage()
        {
            using var scope = _fixture.CreateScope();
            var imageService = scope.ServiceProvider.GetRequiredService<IImageService>();
            var articleService = scope.ServiceProvider.GetRequiredService<IArticleService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var author = await userManager.FindByEmailAsync(DevTestAccounts.AutomationEmail);
            Assert.NotNull(author);

            var article = await articleService.CreateAsync(new Article
            {
                Title = "Reorder Test Article",
                Body = "test body",
                AuthorUserId = author!.Id
            }, author.Id);

            var sourceImagePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "test-image.jpg");
            var image1 = await CreateImageAsync(imageService, articleService, article.Id, sourceImagePath, isMainImage: true);
            var image2 = await CreateImageAsync(imageService, articleService, article.Id, sourceImagePath, isMainImage: false);
            var image3 = await CreateImageAsync(imageService, articleService, article.Id, sourceImagePath, isMainImage: false);

            // Uploaded in order 1, 2, 3 - reverse it.
            await articleService.ReorderArticleImagesAsync(article.Id, new List<int> { image3.Id, image2.Id, image1.Id });

            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var images = await context.ImageAttachments
                    .Where(i => i.ArticleId == article.Id)
                    .ToListAsync();

                Assert.Equal(0, images.Single(i => i.Id == image3.Id).DisplayOrder);
                Assert.Equal(1, images.Single(i => i.Id == image2.Id).DisplayOrder);
                Assert.Equal(2, images.Single(i => i.Id == image1.Id).DisplayOrder);

                Assert.True(images.Single(i => i.Id == image3.Id).IsMainImage);
                Assert.False(images.Single(i => i.Id == image2.Id).IsMainImage);
                Assert.False(images.Single(i => i.Id == image1.Id).IsMainImage);
                Assert.Single(images, i => i.IsMainImage);
            }

            // Restore the original order and reconfirm.
            await articleService.ReorderArticleImagesAsync(article.Id, new List<int> { image1.Id, image2.Id, image3.Id });

            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var images = await context.ImageAttachments
                    .Where(i => i.ArticleId == article.Id)
                    .ToListAsync();

                Assert.Equal(0, images.Single(i => i.Id == image1.Id).DisplayOrder);
                Assert.True(images.Single(i => i.Id == image1.Id).IsMainImage);
                Assert.Single(images, i => i.IsMainImage);
            }

            await articleService.DeleteAsync(article.Id);
        }

        private static async Task<ImageAttachment> CreateImageAsync(
            IImageService imageService, IArticleService articleService, Guid articleId, string sourcePath, bool isMainImage)
        {
            var upload = await imageService.SaveImageFromDiskAsync(sourcePath, Article.ImageSubfolderName);
            return await articleService.AddArticleImageAsync(articleId, upload, isMainImage);
        }
    }
}
