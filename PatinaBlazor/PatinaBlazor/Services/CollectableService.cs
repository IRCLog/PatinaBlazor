using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class CollectableService : ICollectableService
    {
        private readonly ApplicationDbContext _context;

        public CollectableService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Collectable>> GetRecentCollectablesAsync(int count = 10)
        {
            return await _context.Collectables
                .AsNoTracking()
                .Include(c => c.Images)
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedDate)
                .Take(count)
                .ToListAsync();
        }

        public async Task<Collectable?> GetCollectableByIdAsync(Guid id)
        {
            return await _context.Collectables
                .Include(c => c.Images)
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<Collectable>> GetUserCollectablesAsync(string userId)
        {
            return await _context.Collectables
                .AsNoTracking()
                .Include(c => c.Images)
                .Include(c => c.User)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync();
        }
    }
}