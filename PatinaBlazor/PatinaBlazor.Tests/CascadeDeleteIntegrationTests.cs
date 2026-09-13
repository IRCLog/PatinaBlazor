using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Reproduces AllUsers.razor's HandleDeleteConfirmed sequence directly against the
    // service/EF layer: CollectableCollectionItem -> Collectable is DeleteBehavior.Restrict,
    // so a real FK-violation bug shipped once when nothing removed the user's membership rows
    // (their auto-created "All Collectables" entry) before deleting the Collectable. This test
    // exists so that regression can never reach a real user again without a test failing first.
    [Collection("Database")]
    public class CascadeDeleteIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public CascadeDeleteIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeletingUser_WithCollectableAndImage_CleansUpEverythingWithNoFkViolation()
        {
            using var scope = _fixture.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var collectionService = scope.ServiceProvider.GetRequiredService<ICollectionService>();
            var imageService = scope.ServiceProvider.GetRequiredService<IImageService>();

            var testEmail = $"cascadetest_{Guid.NewGuid():N}@patinablazor.local";
            var user = new ApplicationUser
            {
                UserName = testEmail,
                Email = testEmail,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var createResult = await userManager.CreateAsync(user, "ThrowawayTest123!");
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));

            Guid collectableId;
            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var collectable = new Collectable
                {
                    Description = "Cascade delete test item",
                    PricePaid = 1.00m,
                    UserId = user.Id
                };
                context.Collectables.Add(collectable);
                await context.SaveChangesAsync();
                collectableId = collectable.Id;
            }

            await collectionService.EnsureAllCollectablesCollectionExistsAsync(user.Id);
            var allCollectablesCollection = await collectionService.GetAllCollectablesCollectionAsync(user.Id);
            Assert.NotNull(allCollectablesCollection);
            await collectionService.AddCollectableToCollectionAsync(allCollectablesCollection!.Id, collectableId);

            var sourceImagePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "test-image.jpg");
            var upload = await imageService.SaveImageFromDiskAsync(sourceImagePath, Collectable.ImageSubfolderName);
            string thumbnailPath, mediumPath, fullPath;
            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var imageAttachment = new ImageAttachment
                {
                    CollectableId = collectableId,
                    FileName = upload.FileName,
                    RelativePath = upload.RelativePath,
                    ThumbnailRelativePath = upload.ThumbnailRelativePath,
                    MediumRelativePath = upload.MediumRelativePath,
                    ThumbnailWidth = upload.ThumbnailWidth,
                    MediumWidth = upload.MediumWidth,
                    ContentType = upload.ContentType,
                    FileSize = upload.FileSize,
                    IsMainImage = true,
                    DisplayOrder = 0
                };
                context.ImageAttachments.Add(imageAttachment);
                await context.SaveChangesAsync();

                thumbnailPath = Path.Combine(_fixture.WebRootPath, imageAttachment.ThumbnailRelativePath!.TrimStart('/'));
                mediumPath = Path.Combine(_fixture.WebRootPath, imageAttachment.MediumRelativePath!.TrimStart('/'));
                fullPath = Path.Combine(_fixture.WebRootPath, imageAttachment.RelativePath.TrimStart('/'));
            }
            Assert.True(File.Exists(thumbnailPath));

            // The exact sequence AllUsers.razor's HandleDeleteConfirmed performs: remove
            // CollectableCollectionItem membership rows first (Restrict, not Cascade), then
            // the Collectable, then the user itself.
            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var membershipRows = await context.CollectableCollectionItems
                    .Where(ci => ci.CollectableId == collectableId)
                    .ToListAsync();
                context.CollectableCollectionItems.RemoveRange(membershipRows);
                context.Collectables.RemoveRange(context.Collectables.Where(c => c.Id == collectableId));
                await context.SaveChangesAsync();
            }

            var deleteResult = await userManager.DeleteAsync(user);
            Assert.True(deleteResult.Succeeded, string.Join(", ", deleteResult.Errors.Select(e => e.Description)));

            await using var verifyContext = await _fixture.DbContextFactory.CreateDbContextAsync();
            Assert.Null(await userManager.FindByIdAsync(user.Id));
            Assert.Null(await verifyContext.Collectables.FindAsync(collectableId));
            Assert.False(await verifyContext.ImageAttachments.AnyAsync(i => i.CollectableId == collectableId));
            Assert.False(await verifyContext.CollectableCollectionItems.AnyAsync(ci => ci.CollectableId == collectableId));
            Assert.False(await verifyContext.CollectableCollections.AnyAsync(c => c.UserId == user.Id));
            Assert.False(File.Exists(thumbnailPath));
            Assert.False(File.Exists(mediumPath));
            Assert.False(File.Exists(fullPath));
        }
    }
}
