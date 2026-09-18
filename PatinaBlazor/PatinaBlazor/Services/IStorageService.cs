using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
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
