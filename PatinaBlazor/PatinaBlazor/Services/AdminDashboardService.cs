using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Uses IDbContextFactory rather than an injected scoped ApplicationDbContext - see the
    // same note on ArticleService/StorageService/etc. Every method here gets its own
    // short-lived context.
    public class AdminDashboardService : IAdminDashboardService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IStorageService _storageService;

        public AdminDashboardService(IDbContextFactory<ApplicationDbContext> contextFactory, IStorageService storageService)
        {
            _contextFactory = contextFactory;
            _storageService = storageService;
        }

        public async Task<AdminDashboardSummary> GetSummaryAsync(DateTime? asOf = null, int recentActivityCount = 8)
        {
            var now = asOf ?? DateTime.UtcNow;
            var errorWindowStart = now.AddHours(-24);

            await using var context = await _contextFactory.CreateDbContextAsync();

            var summary = new AdminDashboardSummary
            {
                UserCount = await context.Users.CountAsync(),
                CollectableCount = await context.Collectables.CountAsync(),
                TotalArticleCount = await context.Articles.CountAsync(),
                PublishedArticleCount = await context.Articles.CountAsync(a => a.Status == ArticleStatus.Published),
                ErrorCount24h = await context.AppLogs.CountAsync(l =>
                    l.Level == "Error" && l.TimeStamp >= errorWindowStart && l.TimeStamp <= now),
                RecentActivity = await context.AppLogs
                    .Where(l => l.TimeStamp <= now)
                    .OrderByDescending(l => l.TimeStamp)
                    .Take(recentActivityCount)
                    .ToListAsync(),
                Storage = await _storageService.GetDashboardSummaryAsync()
            };

            return summary;
        }
    }
}
