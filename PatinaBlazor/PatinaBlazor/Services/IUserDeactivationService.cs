using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class UserDeactivationResult
    {
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
    }

    public interface IUserDeactivationService
    {
        // Locks or unlocks a user's ability to sign in via ASP.NET Core Identity's own
        // lockout mechanism - never deletes anything, so every StorageRental,
        // StoragePaymentTransaction, Collectable, Article, etc. the user is linked to stays
        // fully intact regardless of which way this is called.
        Task<UserDeactivationResult> SetActiveAsync(ApplicationUser user, bool active);
    }
}
