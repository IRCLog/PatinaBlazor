using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Uses IDbContextFactory rather than an injected scoped ApplicationDbContext - see the
    // same note on ArticleService/StorageService/etc. UserManager/RoleManager manage their own
    // context internally (via the app's IdentityStores registration), independent of this.
    public class StorageCustomerRegistrationService : IStorageCustomerRegistrationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly UserManager<ApplicationUser> _userManager;

        public StorageCustomerRegistrationService(IDbContextFactory<ApplicationDbContext> contextFactory, UserManager<ApplicationUser> userManager)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
        }

        public async Task<StorageCustomerRegistrationResult> RegisterAsync(StorageCustomerRegistrationRequest request)
        {
            var displayName = await ResolveUniqueDisplayNameAsync($"{request.FirstName} {request.LastName}".Trim());

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = displayName,
                CreatedDate = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return new StorageCustomerRegistrationResult
                {
                    Succeeded = false,
                    Errors = createResult.Errors.Select(e => e.Description)
                };
            }

            await _userManager.SetPhoneNumberAsync(user, request.PhoneNumber);
            await _userManager.AddToRoleAsync(user, StorageService.StorageCustomerRoleName);

            await using (var context = await _contextFactory.CreateDbContextAsync())
            {
                context.StorageCustomerProfiles.Add(new StorageCustomerProfile
                {
                    UserId = user.Id,
                    AddressLine1 = request.AddressLine1,
                    AddressLine2 = request.AddressLine2,
                    City = request.City,
                    State = request.State,
                    PostalCode = request.PostalCode,
                    EmergencyContactName = request.EmergencyContactName,
                    EmergencyContactPhone = request.EmergencyContactPhone
                });
                await context.SaveChangesAsync();
            }

            return new StorageCustomerRegistrationResult { Succeeded = true, User = user };
        }

        // DisplayName has a site-wide uniqueness constraint originally meant for a freely-chosen
        // handle (see Register.razor) - but here it's derived from a real name, and real names
        // collide far more often than chosen handles. Rather than reject a legitimate second
        // "John Smith", silently find a free variant by appending an incrementing suffix.
        private async Task<string> ResolveUniqueDisplayNameAsync(string baseName)
        {
            if (!await _userManager.Users.AnyAsync(u => u.DisplayName == baseName))
            {
                return baseName;
            }

            var suffix = 2;
            while (true)
            {
                var candidate = $"{baseName} {suffix}";
                if (!await _userManager.Users.AnyAsync(u => u.DisplayName == candidate))
                {
                    return candidate;
                }
                suffix++;
            }
        }
    }
}
