using Microsoft.AspNetCore.Identity;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class DatabaseSeeder
    {
        private const string AdminRoleName = "Admin";
        private const string StorageAdminRoleName = "Storage Admin";

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ICollectionService _collectionService;
        private readonly IStorageService _storageService;
        private readonly ILogger<DatabaseSeeder> _logger;

        public DatabaseSeeder(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ICollectionService collectionService,
            IStorageService storageService,
            ILogger<DatabaseSeeder> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _collectionService = collectionService;
            _storageService = storageService;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            try
            {
                await EnsureRoleExistsAsync(AdminRoleName);
                await EnsureRoleExistsAsync(StorageAdminRoleName);
                await EnsureRoleExistsAsync(StorageService.StorageCustomerRoleName);

                var adminUser = await EnsureAdminUserAsync();
                if (adminUser == null)
                {
                    return;
                }

                var customerUserIds = await EnsureDummyStorageCustomersAsync();
                await _storageService.SeedDummyDataAsync(adminUser.Id, customerUserIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while seeding the database");
            }
        }

        private async Task EnsureRoleExistsAsync(string roleName)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
            {
                return;
            }

            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (result.Succeeded)
            {
                _logger.LogInformation("{RoleName} role created successfully", roleName);
            }
            else
            {
                _logger.LogError("Failed to create {RoleName} role", roleName);
                foreach (var error in result.Errors)
                {
                    _logger.LogError("- {ErrorDescription}", error.Description);
                }
            }
        }

        private async Task<ApplicationUser?> EnsureAdminUserAsync()
        {
            // Check if Adam user already exists
            var existingUser = await _userManager.FindByEmailAsync("adamsilzell@gmail.com");
            if (existingUser != null)
            {
                // Assign Admin role if not already assigned
                if (!await _userManager.IsInRoleAsync(existingUser, AdminRoleName))
                {
                    var roleResult = await _userManager.AddToRoleAsync(existingUser, AdminRoleName);
                    if (roleResult.Succeeded)
                    {
                        _logger.LogInformation("Admin role assigned to existing user Adam");
                    }
                    else
                    {
                        _logger.LogError("Failed to assign Admin role to existing user");
                    }
                }
                _logger.LogInformation("User Adam already exists, skipping creation.");
                return existingUser;
            }

            // Create new user
            var user = new ApplicationUser
            {
                UserName = "adamsilzell@gmail.com", // Use email as username for login compatibility
                Email = "adamsilzell@gmail.com",
                EmailConfirmed = true, // Set to true to bypass email confirmation
                CreatedDate = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, "k33e8Vgrayson!");

            if (result.Succeeded)
            {
                // Assign Admin role to the new user
                var roleResult = await _userManager.AddToRoleAsync(user, AdminRoleName);
                if (roleResult.Succeeded)
                {
                    _logger.LogInformation("User Adam created successfully with email: {Email} and assigned Admin role", user.Email);
                }
                else
                {
                    _logger.LogError("User created but failed to assign Admin role");
                }

                // Create "All Collectables" collection for the new user
                await _collectionService.EnsureAllCollectablesCollectionExistsAsync(user.Id);
                _logger.LogInformation("Created 'All Collectables' collection for user Adam");

                return user;
            }

            _logger.LogError("Failed to create user Adam:");
            foreach (var error in result.Errors)
            {
                _logger.LogError("- {ErrorDescription}", error.Description);
            }
            return null;
        }

        private async Task<List<string>> EnsureDummyStorageCustomersAsync()
        {
            var dummyCustomers = new[]
            {
                ("storage.customer1@example.com", "Storage Customer One"),
                ("storage.customer2@example.com", "Storage Customer Two"),
                ("storage.customer3@example.com", "Storage Customer Three")
            };

            var customerIds = new List<string>();

            foreach (var (email, displayName) in dummyCustomers)
            {
                var existing = await _userManager.FindByEmailAsync(email);
                if (existing != null)
                {
                    customerIds.Add(existing.Id);
                    continue;
                }

                var user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    DisplayName = displayName,
                    EmailConfirmed = true,
                    CreatedDate = DateTime.UtcNow
                };

                var result = await _userManager.CreateAsync(user, "Str0ngDummyPass!");
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, StorageService.StorageCustomerRoleName);
                    customerIds.Add(user.Id);
                    _logger.LogInformation("Created dummy storage customer {Email}", email);
                }
                else
                {
                    _logger.LogError("Failed to create dummy storage customer {Email}:", email);
                    foreach (var error in result.Errors)
                    {
                        _logger.LogError("- {ErrorDescription}", error.Description);
                    }
                }
            }

            return customerIds;
        }
    }
}
