using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Uses IDbContextFactory rather than an injected scoped ApplicationDbContext: Blazor
    // Server keeps one DI scope (and one scoped DbContext) alive for a circuit's entire
    // lifetime, not per page, so a query from the page a user just left can still be
    // in-flight when the next page's query starts - and EF Core's DbContext isn't safe
    // for concurrent use. Every method here gets its own short-lived context instead.
    public class CollectableService : ICollectableService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public CollectableService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<Collectable>> GetRecentCollectablesAsync(int count = 10)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Collectables
                .AsNoTracking()
                .Include(c => c.Images)
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedDate)
                .Take(count)
                .ToListAsync();
        }

        public async Task<Collectable?> GetCollectableByIdAsync(Guid id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Collectables
                .Include(c => c.Images)
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<Collectable>> GetUserCollectablesAsync(string userId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Collectables
                .AsNoTracking()
                .Include(c => c.Images)
                .Include(c => c.User)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync();
        }
    }
}
