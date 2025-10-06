using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class CollectionService : ICollectionService
    {
        private readonly ApplicationDbContext _context;

        public CollectionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<CollectableCollection>> GetUserCollectionsAsync(string userId)
        {
            return await _context.CollectableCollections
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
            return await _context.CollectableCollections
                .Include(c => c.CollectableItems)
                .ThenInclude(ci => ci.Collectable)
                .ThenInclude(c => c.Images)
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<CollectableCollection> CreateCollectionAsync(string name, string userId)
        {
            var collection = new CollectableCollection
            {
                Id = Guid.NewGuid(),
                Name = name,
                UserId = userId,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            _context.CollectableCollections.Add(collection);
            await _context.SaveChangesAsync();
            return collection;
        }

        public async Task UpdateCollectionAsync(CollectableCollection collection)
        {
            collection.ModifiedDate = DateTime.UtcNow;
            _context.CollectableCollections.Update(collection);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteCollectionAsync(Guid id)
        {
            var collection = await _context.CollectableCollections.FindAsync(id);
            if (collection != null && !collection.IsSystemCollection)
            {
                _context.CollectableCollections.Remove(collection);
                await _context.SaveChangesAsync();
            }
        }

        public async Task AddCollectableToCollectionAsync(Guid collectionId, Guid collectableId)
        {
            // Check if already exists
            var exists = await _context.CollectableCollectionItems
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

                _context.CollectableCollectionItems.Add(item);

                // Update collection modified date
                var collection = await _context.CollectableCollections.FindAsync(collectionId);
                if (collection != null)
                {
                    collection.ModifiedDate = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task RemoveCollectableFromCollectionAsync(Guid collectionId, Guid collectableId)
        {
            var item = await _context.CollectableCollectionItems
                .FirstOrDefaultAsync(ci => ci.CollectableCollectionId == collectionId && ci.CollectableId == collectableId);

            if (item != null)
            {
                _context.CollectableCollectionItems.Remove(item);

                // Update collection modified date
                var collection = await _context.CollectableCollections.FindAsync(collectionId);
                if (collection != null)
                {
                    collection.ModifiedDate = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<Collectable>> GetCollectablesInCollectionAsync(Guid collectionId)
        {
            return await _context.CollectableCollectionItems
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
            var existingCollection = await _context.CollectableCollections
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

                _context.CollectableCollections.Add(collection);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<CollectableCollection?> GetAllCollectablesCollectionAsync(string userId)
        {
            return await _context.CollectableCollections
                .FirstOrDefaultAsync(c => c.UserId == userId && c.IsSystemCollection);
        }
    }
}
