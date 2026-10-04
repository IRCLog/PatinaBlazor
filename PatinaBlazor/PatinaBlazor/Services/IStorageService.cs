using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class CancelRentalResult
    {
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
    }

    public interface IStorageService
    {
        // Properties
        Task<List<StorageProperty>> GetPropertiesAsync();
        Task<StorageProperty?> GetPropertyByIdAsync(int id);
        Task<StorageProperty> CreatePropertyAsync(StorageProperty property, string currentUserId);
        Task UpdatePropertyAsync(StorageProperty property, string currentUserId);
        Task DeletePropertyAsync(int id);
        Task<ImageAttachment> AddPropertyImageAsync(int propertyId, ImageUploadResult upload, bool isMainImage);
        Task<ImageAttachment?> GetPropertyImageAsync(int imageId);
        Task DeletePropertyImageAsync(int imageId);

        // Units
        Task<List<StorageUnit>> GetUnitsForPropertyAsync(int propertyId);
        Task<StorageUnit?> GetUnitByIdAsync(int id);
        Task<StorageUnit> CreateUnitAsync(StorageUnit unit, string currentUserId);
        Task UpdateUnitAsync(StorageUnit unit, string currentUserId);
        Task DeleteUnitAsync(int id);

        // Rentals
        Task<StorageRental> StartRentalAsync(int unitId, string customerUserId, decimal monthlyRate, DateTime startDate, BillingFrequency billingFrequency, DateTime paymentDate, string currentUserId);
        Task EndRentalAsync(int rentalId, DateTime endDate, string currentUserId);

        // The customer-facing equivalent of EndRentalAsync above - deliberately separate,
        // not a thin wrapper, because EndRentalAsync has no ownership check at all (safe
        // only because it's admin-gated today). This verifies the rental actually belongs
        // to customerUserId before doing anything, returning a friendly failure rather than
        // throwing - reachable directly from /storage/account, so a wrong/guessed rentalId
        // must never silently succeed or throw a raw exception. Informational only: never
        // triggers a charge, even if GetFinalBillingPreview shows an amount still owed.
        Task<CancelRentalResult> CancelOwnRentalAsync(int rentalId, DateTime moveOutDate, string customerUserId);

        Task<StorageRental?> GetActiveRentalForUnitAsync(int unitId);
        Task<StorageRental?> GetRentalByIdAsync(int rentalId);

        // Every rental a customer has ever had (Unit/Property included), newest first - the
        // wallet page (/storage/account) picks the most relevant one from this itself, since
        // "current" depends on whether they have an in-progress or past rental.
        Task<List<StorageRental>> GetRentalsForCustomerAsync(string customerUserId);

        // Dashboard aggregation
        Task<StorageDashboardSummary> GetDashboardSummaryAsync();
        Task<List<MonthlyRevenuePoint>> GetMonthlyRevenueAsync(int monthsBack = 12, int monthsForwardProjection = 3);

        // Dev seed hook, called only from DatabaseSeeder
        Task SeedDummyDataAsync(string adminUserId, List<string> customerUserIds);
    }
}
