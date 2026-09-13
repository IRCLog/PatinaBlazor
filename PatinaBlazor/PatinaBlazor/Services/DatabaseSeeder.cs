using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class DatabaseSeeder
    {
        private const string AdminRoleName = "Admin";
        private const string StorageAdminRoleName = "Storage Admin";
        private const string ArticlePublisherRoleName = "Article Publisher";

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ICollectionService _collectionService;
        private readonly IStorageService _storageService;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<DatabaseSeeder> _logger;

        public DatabaseSeeder(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ICollectionService collectionService,
            IStorageService storageService,
            IHostEnvironment environment,
            ILogger<DatabaseSeeder> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _collectionService = collectionService;
            _storageService = storageService;
            _environment = environment;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            try
            {
                await EnsureRoleExistsAsync(AdminRoleName);
                await EnsureRoleExistsAsync(StorageAdminRoleName);
                await EnsureRoleExistsAsync(StorageService.StorageCustomerRoleName);
                await EnsureRoleExistsAsync(ArticlePublisherRoleName);

                var adminUser = await EnsureAdminUserAsync();
                if (adminUser == null)
                {
                    return;
                }

                var customerUserIds = await EnsureDummyStorageCustomersAsync();
                await _storageService.SeedDummyDataAsync(adminUser.Id, customerUserIds);

                // Never in Production - these accounts' credentials are public (checked into
                // source, see DevTestAccounts.cs), used by interactive dev-time testing and
                // by PatinaBlazor.Tests' Testcontainers fixture (which calls this same
                // SeedAsync method against its own throwaway DB after migrating it).
                if (!_environment.IsProduction())
                {
                    await EnsureDevTestAccountsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while seeding the database");
            }
        }

        private async Task EnsureDevTestAccountsAsync()
        {
            // One per role, plus one with no role at all - deliberately separate from the
            // real seeded admin (adamsilzell@gmail.com) and from EnsureDummyStorageCustomersAsync's
            // accounts, so admin/role-gated paths can be exercised in tests or interactively
            // without ever touching real credentials.
            await EnsureDevTestAccountAsync(DevTestAccounts.AutomationEmail, role: null);
            await EnsureDevTestAccountAsync(DevTestAccounts.AdminEmail, AdminRoleName);
            await EnsureDevTestAccountAsync(DevTestAccounts.StorageAdminEmail, StorageAdminRoleName);
            await EnsureDevTestAccountAsync(DevTestAccounts.StorageCustomerEmail, StorageService.StorageCustomerRoleName);
            await EnsureDevTestAccountAsync(DevTestAccounts.ArticlePublisherEmail, ArticlePublisherRoleName);
        }

        private async Task EnsureDevTestAccountAsync(string email, string? role)
        {
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
            {
                if (role != null && !await _userManager.IsInRoleAsync(existing, role))
                {
                    await _userManager.AddToRoleAsync(existing, role);
                }
                return;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = role == null ? "Dev Test Account" : $"Dev Test ({role})",
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, DevTestAccounts.Password);
            if (result.Succeeded)
            {
                if (role != null)
                {
                    await _userManager.AddToRoleAsync(user, role);
                }
                await _collectionService.EnsureAllCollectablesCollectionExistsAsync(user.Id);
                _logger.LogInformation("Created dev/test account {Email} (role: {Role})", email, role ?? "none");
            }
            else
            {
                _logger.LogError("Failed to create dev/test account {Email}:", email);
                foreach (var error in result.Errors)
                {
                    _logger.LogError("- {ErrorDescription}", error.Description);
                }
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
