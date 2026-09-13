using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Confirms DatabaseSeeder.EnsureDevTestAccountsAsync actually creates every expected
    // account with the correct role - not just that the fixture's own seeding call in
    // InitializeAsync didn't throw (the other tests only depend on AutomationEmail existing,
    // so a broken role assignment for one of the others could go unnoticed otherwise).
    [Collection("Database")]
    public class DevTestAccountsSeedingTests
    {
        private readonly DatabaseFixture _fixture;

        public DevTestAccountsSeedingTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(DevTestAccounts.AutomationEmail, null)]
        [InlineData(DevTestAccounts.AdminEmail, "Admin")]
        [InlineData(DevTestAccounts.StorageAdminEmail, "Storage Admin")]
        [InlineData(DevTestAccounts.StorageCustomerEmail, "Storage Customer")]
        [InlineData(DevTestAccounts.ArticlePublisherEmail, "Article Publisher")]
        public async Task SeededAccount_ExistsWithExpectedRole(string email, string? expectedRole)
        {
            using var scope = _fixture.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            var roles = await userManager.GetRolesAsync(user!);
            if (expectedRole is null)
            {
                Assert.Empty(roles);
            }
            else
            {
                Assert.Contains(expectedRole, roles);
            }
        }
    }
}
