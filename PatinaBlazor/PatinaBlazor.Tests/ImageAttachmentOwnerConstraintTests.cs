using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers the CK_ImageAttachment_ExactlyOneOwner DB check constraint (see
    // ApplicationDbContext.OnModelCreating) - previously confirmed only via ad-hoc raw SQL
    // during development (see CLAUDE.md's 2026-09-06 Articles entry, which even flags a false
    // "passed" reading it got once from an INSERT...SELECT against an empty source table).
    // Permanent coverage that a dual-owner or zero-owner row can never actually be persisted,
    // regardless of how many owner types ImageAttachment grows in the future.
    [Collection("Database")]
    public class ImageAttachmentOwnerConstraintTests
    {
        private readonly DatabaseFixture _fixture;

        public ImageAttachmentOwnerConstraintTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task InsertWithZeroOwnersSet_IsRejected()
        {
            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            context.ImageAttachments.Add(NewBareImage());

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task InsertWithTwoOwnersSet_IsRejected()
        {
            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();

            var collectable = new Collectable
            {
                Description = "Owner constraint test item",
                PricePaid = 1.00m,
                UserId = (await context.Users.FirstAsync()).Id
            };
            var article = new Article
            {
                Title = "Owner Constraint Test Article",
                Body = "body",
                AuthorUserId = collectable.UserId
            };
            context.Collectables.Add(collectable);
            context.Articles.Add(article);
            await context.SaveChangesAsync();

            var image = NewBareImage();
            image.CollectableId = collectable.Id;
            image.ArticleId = article.Id;
            context.ImageAttachments.Add(image);

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task InsertWithExactlyOneOwnerSet_Succeeds()
        {
            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();

            var article = new Article
            {
                Title = "Single Owner Test Article",
                Body = "body",
                AuthorUserId = (await context.Users.FirstAsync()).Id
            };
            context.Articles.Add(article);
            await context.SaveChangesAsync();

            var image = NewBareImage();
            image.ArticleId = article.Id;
            context.ImageAttachments.Add(image);

            await context.SaveChangesAsync();

            Assert.True(await context.ImageAttachments.AnyAsync(i => i.Id == image.Id));
        }

        private static ImageAttachment NewBareImage() => new()
        {
            FileName = "test.jpg",
            RelativePath = "/uploads/test/test.jpg",
            ContentType = "image/jpeg",
            FileSize = 123
        };
    }
}
