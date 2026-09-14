using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public interface IAdminDashboardService
    {
        // asOf anchors the "Errors in the last 24 hours" window - defaults to DateTime.UtcNow,
        // but is settable so tests can pin the boundary precisely (same pattern as
        // StorageRental.GetNextBillingDate's asOf parameter).
        Task<AdminDashboardSummary> GetSummaryAsync(DateTime? asOf = null, int recentActivityCount = 8);
    }
}
