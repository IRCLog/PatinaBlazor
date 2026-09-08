using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Uses IDbContextFactory rather than an injected scoped ApplicationDbContext: Blazor
    // Server keeps one DI scope (and one scoped DbContext) alive for a circuit's entire
    // lifetime, not per page, so a query from the page a user just left can still be
    // in-flight when the next page's query starts - and EF Core's DbContext isn't safe
    // for concurrent use. Every method here gets its own short-lived context instead.
    public class CollectionService : ICollectionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public CollectionService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<CollectableCollection>> GetUserCollectionsAsync(string userId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.CollectableCollections
                .AsNoTracking()
                .Include(c => c.CollectableItems)
                .ThenInclude(ci => ci.Collectable)
                .ThenInclude(c => c.Images)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.IsSystemCollection)
                .ThenByDescending(c => c.ModifiedDate)
                .ToListAsync();
        }

        public async Task<CollectableCollection?> GetCollectionByIdAsync(Guid id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.CollectableCollections
                .Include(c => c.CollectableItems)
                .ThenInclude(ci => ci.Collectable)
                .ThenInclude(c => c.Images)
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<CollectableCollection> CreateCollectionAsync(string name, string userId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var collection = new CollectableCollection
            {
                Id = Guid.NewGuid(),
                Name = name,
                UserId = userId,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            context.CollectableCollections.Add(collection);
            await context.SaveChangesAsync();
            return collection;
        }

        public async Task UpdateCollectionAsync(CollectableCollection collection)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            collection.ModifiedDate = DateTime.UtcNow;
            context.CollectableCollections.Update(collection);
            await context.SaveChangesAsync();
        }

        public async Task DeleteCollectionAsync(Guid id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var collection = await context.CollectableCollections.FindAsync(id);
            if (collection != null && !collection.IsSystemCollection)
            {
                context.CollectableCollections.Remove(collection);
                await context.SaveChangesAsync();
            }
        }

        public async Task AddCollectableToCollectionAsync(Guid collectionId, Guid collectableId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Check if already exists
            var exists = await context.CollectableCollectionItems
                .AnyAsync(ci => ci.CollectableCollectionId == collectionId && ci.CollectableId == collectableId);

            if (!exists)
            {
                var item = new CollectableCollectionItem
                {
                    Id = Guid.NewGuid(),
                    CollectableCollectionId = collectionId,
                    CollectableId = collectableId,
                    AddedDate = DateTime.UtcNow
                };

                context.CollectableCollectionItems.Add(item);

                // Update collection modified date
                var collection = await context.CollectableCollections.FindAsync(collectionId);
                if (collection != null)
                {
                    collection.ModifiedDate = DateTime.UtcNow;
                }

                await context.SaveChangesAsync();
            }
        }

        public async Task RemoveCollectableFromCollectionAsync(Guid collectionId, Guid collectableId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var item = await context.CollectableCollectionItems
                .FirstOrDefaultAsync(ci => ci.CollectableCollectionId == collectionId && ci.CollectableId == collectableId);

            if (item != null)
            {
                context.CollectableCollectionItems.Remove(item);

                // Update collection modified date
                var collection = await context.CollectableCollections.FindAsync(collectionId);
                if (collection != null)
                {
                    collection.ModifiedDate = DateTime.UtcNow;
                }

                await context.SaveChangesAsync();
            }
        }

        public async Task<List<Collectable>> GetCollectablesInCollectionAsync(Guid collectionId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.CollectableCollectionItems
                .AsNoTracking()
                .Where(ci => ci.CollectableCollectionId == collectionId)
                .Include(ci => ci.Collectable)
                .ThenInclude(c => c.Images)
                .Include(ci => ci.Collectable)
                .ThenInclude(c => c.User)
                .OrderByDescending(ci => ci.AddedDate)
                .Select(ci => ci.Collectable)
                .ToListAsync();
        }

        public async Task EnsureAllCollectablesCollectionExistsAsync(string userId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var existingCollection = await context.CollectableCollections
                .FirstOrDefaultAsync(c => c.UserId == userId && c.IsSystemCollection);

            if (existingCollection == null)
            {
                var collection = new CollectableCollection
                {
                    Id = Guid.NewGuid(),
                    Name = "All Collectables",
                    UserId = userId,
                    IsSystemCollection = true,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };

                context.CollectableCollections.Add(collection);
                await context.SaveChangesAsync();
            }
        }

        public async Task<CollectableCollection?> GetAllCollectablesCollectionAsync(string userId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.CollectableCollections
                .FirstOrDefaultAsync(c => c.UserId == userId && c.IsSystemCollection);
        }
    }
}
