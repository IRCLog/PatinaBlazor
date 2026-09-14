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

            var recentActivity = await context.AppLogs
                .Where(l => l.TimeStamp <= now)
                .OrderByDescending(l => l.TimeStamp)
                .Take(recentActivityCount)
                .ToListAsync();

            var summary = new AdminDashboardSummary
            {
                UserCount = await context.Users.CountAsync(),
                CollectableCount = await context.Collectables.CountAsync(),
                TotalArticleCount = await context.Articles.CountAsync(),
                PublishedArticleCount = await context.Articles.CountAsync(a => a.Status == ArticleStatus.Published),
                ErrorCount24h = await context.AppLogs.CountAsync(l =>
                    l.Level == "Error" && l.TimeStamp >= errorWindowStart && l.TimeStamp <= now),
                RecentActivity = recentActivity,
                RecentActivityUserDisplayNames = await ResolveUserDisplayNamesAsync(context, recentActivity),
                Storage = await _storageService.GetDashboardSummaryAsync()
            };

            return summary;
        }

        // AppLogEntry.UserId is just whatever raw id string a log call happened to pass as a
        // structured {UserId} property (see SecurityLoggerExtensions/EntityLogicUnit) - Logs
        // is a Serilog-owned table with no FK relationship to AspNetUsers, so there's no join
        // to lean on. A single batched lookup for the distinct ids actually present in this
        // page's small Recent Activity list is cheap and avoids an N+1 query per row.
        private static async Task<Dictionary<string, string>> ResolveUserDisplayNamesAsync(
            ApplicationDbContext context, List<AppLogEntry> recentActivity)
        {
            var userIds = recentActivity
                .Where(l => !string.IsNullOrEmpty(l.UserId))
                .Select(l => l.UserId!)
                .Distinct()
                .ToList();

            if (userIds.Count == 0)
            {
                return new Dictionary<string, string>();
            }

            // Same fallback order AllUsers.razor's own GetDisplayName helper uses -
            // DisplayName is the friendliest identifier when set, UserName/Email otherwise.
            return await context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName! : (u.UserName ?? u.Email ?? u.Id));
        }
    }
}
