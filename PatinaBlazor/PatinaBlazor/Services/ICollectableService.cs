using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public interface ICollectableService
    {
        Task<List<Collectable>> GetRecentCollectablesAsync(int count = 10);
        Task<Collectable?> GetCollectableByIdAsync(Guid id);
        Task<List<Collectable>> GetUserCollectablesAsync(string userId);
    }
}