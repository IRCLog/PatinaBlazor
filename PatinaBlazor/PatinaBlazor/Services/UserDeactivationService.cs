using Microsoft.AspNetCore.Identity;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Deleting a user from the admin UI used to call UserManager.DeleteAsync directly, which
    // threw a raw DbUpdateException the moment the target had any StorageRental/
    // StorageProperty/StorageUnit/Article row referencing them - those FKs are deliberately
    // DeleteBehavior.Restrict, to preserve rental/revenue/authorship history rather than
    // silently cascading it away (see ApplicationDbContext.cs's own comments on each). Per
    // explicit user direction, "deleting" a user from the admin UI no longer deletes anything
    // at all - it locks the account out instead, via ASP.NET Core Identity's real lockout
    // mechanism (LockoutEnabled/LockoutEnd - the exact fields SignInManager's
    // result.IsLockedOut already checks on every Login/2FA/external-login page in this app).
    // This is deliberately NOT the same as ApplicationUser's separate IsLocked column (set by
    // the admin "Account Locked" toggle) - that field is never actually read at sign-in time,
    // so reusing it would silently fail to block anything; real Identity lockout is the only
    // mechanism in this app that's confirmed to actually work.
    public class UserDeactivationService : IUserDeactivationService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UserDeactivationService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<UserDeactivationResult> SetActiveAsync(ApplicationUser user, bool active)
        {
            // LockoutEnabled must be true for LockoutEnd to actually take effect -
            // IsLockedOutAsync (and therefore SignInManager) gates on both together, so this
            // is set unconditionally rather than only on deactivation.
            var enableResult = await _userManager.SetLockoutEnabledAsync(user, true);
            if (!enableResult.Succeeded)
            {
                return Fail(enableResult);
            }

            var endDateResult = await _userManager.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);
            if (!endDateResult.Succeeded)
            {
                return Fail(endDateResult);
            }

            return new UserDeactivationResult { Succeeded = true };
        }

        private static UserDeactivationResult Fail(IdentityResult result) => new()
        {
            Succeeded = false,
            Error = string.Join(", ", result.Errors.Select(e => e.Description))
        };
    }
}
