namespace PatinaBlazor.Data
{
    // Backs Admin.razor's stat cards and Recent Activity list - pulled out of the page's own
    // code-behind into AdminDashboardService so the counts/window math are unit-testable
    // without a browser, the same reasoning StorageService.GetDashboardSummaryAsync already
    // follows for the Storage dashboard.
    public class AdminDashboardSummary
    {
        public int UserCount { get; set; }
        public int CollectableCount { get; set; }
        public int TotalArticleCount { get; set; }
        public int PublishedArticleCount { get; set; }

        // Errors logged in the 24 hours up to (and including) the moment GetSummaryAsync was
        // called - see AdminDashboardService for the exact window boundary.
        public int ErrorCount24h { get; set; }

        public StorageDashboardSummary Storage { get; set; } = new();

        public List<AppLogEntry> RecentActivity { get; set; } = new();

        // Keyed by AppLogEntry.UserId, for rows in RecentActivity that have one - resolved
        // once via a single batched lookup rather than per-row, see AdminDashboardService.
        public Dictionary<string, string> RecentActivityUserDisplayNames { get; set; } = new();
    }
}
