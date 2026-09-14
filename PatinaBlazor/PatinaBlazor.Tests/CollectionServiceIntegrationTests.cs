using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers two CollectionService behaviors previously verified once via a throwaway console
    // harness and then deleted (see CLAUDE.md's 2026-09-12 EntityLogicUnit entry, which
    // exercised every CollectionService method this way): AddCollectableToCollectionAsync's
    // dedupe check (adding the same Collectable to a collection twice must not create two
    // membership rows), and DeleteCollectionAsync's refusal to delete a user's auto-created
    // system ("All Collectables") collection.
    [Collection("Database")]
    public class CollectionServiceIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public CollectionServiceIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task AddCollectableToCollectionAsync_CalledTwiceForSameCollectable_OnlyCreatesOneMembershipRow()
        {
            using var scope = _fixture.CreateScope();
            var collectionService = scope.ServiceProvider.GetRequiredService<ICollectionService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var userId = await CreateThrowawayUserAsync(userManager);
            var collection = await collectionService.CreateCollectionAsync("Dedupe Test Collection", userId);
            var collectableId = await CreateThrowawayCollectableAsync(userId);

            await collectionService.AddCollectableToCollectionAsync(collection.Id, collectableId);
            await collectionService.AddCollectableToCollectionAsync(collection.Id, collectableId);

            var items = await collectionService.GetCollectablesInCollectionAsync(collection.Id);
            Assert.Single(items);
        }

        [Fact]
        public async Task DeleteCollectionAsync_OnSystemCollection_IsRefusedAndCollectionSurvives()
        {
            using var scope = _fixture.CreateScope();
            var collectionService = scope.ServiceProvider.GetRequiredService<ICollectionService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var userId = await CreateThrowawayUserAsync(userManager);
            await collectionService.EnsureAllCollectablesCollectionExistsAsync(userId);
            var systemCollection = await collectionService.GetAllCollectablesCollectionAsync(userId);
            Assert.NotNull(systemCollection);

            await collectionService.DeleteCollectionAsync(systemCollection!.Id);

            var stillThere = await collectionService.GetCollectionByIdAsync(systemCollection.Id);
            Assert.NotNull(stillThere);
        }

        [Fact]
        public async Task DeleteCollectionAsync_OnOrdinaryCollection_RemovesIt()
        {
            using var scope = _fixture.CreateScope();
            var collectionService = scope.ServiceProvider.GetRequiredService<ICollectionService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var userId = await CreateThrowawayUserAsync(userManager);
            var collection = await collectionService.CreateCollectionAsync("Deletable Collection", userId);

            await collectionService.DeleteCollectionAsync(collection.Id);

            Assert.Null(await collectionService.GetCollectionByIdAsync(collection.Id));
        }

        private static async Task<string> CreateThrowawayUserAsync(UserManager<ApplicationUser> userManager)
        {
            var email = $"collectiontest_{Guid.NewGuid():N}@patinablazor.local";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var createResult = await userManager.CreateAsync(user, "ThrowawayTest123!");
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return user.Id;
        }

        private async Task<Guid> CreateThrowawayCollectableAsync(string userId)
        {
            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var collectable = new Collectable
            {
                Description = "Collection service test item",
                PricePaid = 1.00m,
                UserId = userId
            };
            context.Collectables.Add(collectable);
            await context.SaveChangesAsync();
            return collectable.Id;
        }
    }
}
