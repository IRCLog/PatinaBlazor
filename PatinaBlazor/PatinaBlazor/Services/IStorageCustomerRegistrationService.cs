using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public record StorageCustomerRegistrationRequest(
        string Email,
        string Password,
        string FirstName,
        string LastName,
        string PhoneNumber,
        string AddressLine1,
        string? AddressLine2,
        string City,
        string State,
        string PostalCode,
        string EmergencyContactName,
        string EmergencyContactPhone);

    public class StorageCustomerRegistrationResult
    {
        public bool Succeeded { get; init; }
        public ApplicationUser? User { get; init; }
        public IEnumerable<string> Errors { get; init; } = [];
    }

    public interface IStorageCustomerRegistrationService
    {
        Task<StorageCustomerRegistrationResult> RegisterAsync(StorageCustomerRegistrationRequest request);
    }
}
