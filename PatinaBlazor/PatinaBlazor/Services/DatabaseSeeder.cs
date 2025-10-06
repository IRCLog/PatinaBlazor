using Microsoft.AspNetCore.Identity;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class DatabaseSeeder
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ICollectionService _collectionService;
        private readonly ILogger<DatabaseSeeder> _logger;

        public DatabaseSeeder(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ICollectionService collectionService, ILogger<DatabaseSeeder> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _collectionService = collectionService;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            try
            {
                // Create Admin role if it doesn't exist
                const string adminRoleName = "Admin";
                if (!await _roleManager.RoleExistsAsync(adminRoleName))
                {
                    var adminRole = new IdentityRole(adminRoleName);
                    var roleResult = await _roleManager.CreateAsync(adminRole);
                    
                    if (roleResult.Succeeded)
                    {
                        _logger.LogInformation("Admin role created successfully");
                    }
                    else
                    {
                        _logger.LogError("Failed to create Admin role");
                        foreach (var error in roleResult.Errors)
                        {
                            _logger.LogError("- {ErrorDescription}", error.Description);
                        }
                    }
                }

                // Check if Adam user already exists
                var existingUser = await _userManager.FindByEmailAsync("adamsilzell@gmail.com");
                if (existingUser != null)
                {
                    // Assign Admin role if not already assigned
                    if (!await _userManager.IsInRoleAsync(existingUser, adminRoleName))
                    {
                        var roleResult = await _userManager.AddToRoleAsync(existingUser, adminRoleName);
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
                    return;
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
                    var roleResult = await _userManager.AddToRoleAsync(user, adminRoleName);
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
                }
                else
                {
                    _logger.LogError("Failed to create user Adam:");
                    foreach (var error in result.Errors)
                    {
                        _logger.LogError("- {ErrorDescription}", error.Description);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while seeding the database with user Adam");
            }
        }
    }
}