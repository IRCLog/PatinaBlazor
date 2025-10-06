using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public interface ICollectionService
    {
        Task<List<CollectableCollection>> GetUserCollectionsAsync(string userId);
        Task<CollectableCollection?> GetCollectionByIdAsync(Guid id);
        Task<CollectableCollection> CreateCollectionAsync(string name, string userId);
        Task UpdateCollectionAsync(CollectableCollection collection);
        Task DeleteCollectionAsync(Guid id);
        Task AddCollectableToCollectionAsync(Guid collectionId, Guid collectableId);
        Task RemoveCollectableFromCollectionAsync(Guid collectionId, Guid collectableId);
        Task<List<Collectable>> GetCollectablesInCollectionAsync(Guid collectionId);
        Task EnsureAllCollectablesCollectionExistsAsync(string userId);
        Task<CollectableCollection?> GetAllCollectablesCollectionAsync(string userId);
    }
}
