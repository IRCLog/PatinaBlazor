using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Exercises the real /storage/signup form end to end: fill it out, confirm via the real
    // Mailpit-delivered email (same MailpitClient used by RegistrationEmailConfirmationTests/
    // PasswordResetEmailTests, for the same reason - the link only exists once it's gone
    // through the real EmailTemplateRenderer/IdentitySmtpEmailSender pipeline), then verify
    // directly against the DB that everything this flow is supposed to do actually happened:
    // email confirmed, Storage Customer role assigned, profile row persisted with the
    // submitted values - not just that the UI showed a success message.
    [Collection("E2E")]
    public class StorageCustomerSignUpTests
    {
        private readonly WebAppFixture _fixture;

        public StorageCustomerSignUpTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task SignUp_ThenClickRealEmailedLink_ConfirmsAccountAssignsRoleAndPersistsProfile()
        {
            var email = $"e2e-storagesignup-{Guid.NewGuid():N}@example.com";
            const string password = "E2eTest123!";

            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/storage/signup");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("First name").FillAsync("Riley");
            await page.GetByPlaceholder("Last name").FillAsync($"E2E{Guid.NewGuid():N}"[..10]);
            await page.GetByPlaceholder("password", new() { Exact = true }).FillAsync(password);
            await page.GetByPlaceholder("confirm password").FillAsync(password);
            await page.GetByPlaceholder("Phone number").FillAsync("555-0123");
            await page.GetByPlaceholder("Address line 1").FillAsync("456 Harbor Way");
            await page.GetByPlaceholder("City").FillAsync("Bakersfield");
            await page.GetByPlaceholder("State").FillAsync("CA");
            await page.GetByPlaceholder("Postal code").FillAsync("93301");
            await page.GetByPlaceholder("Emergency contact name").FillAsync("Sam Backup");
            await page.GetByPlaceholder("Emergency contact phone").FillAsync("555-0199");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign Up" }).ClickAsync();

            await page.WaitForURLAsync(url => url.Contains("/Account/RegisterConfirmation"), new() { Timeout = 10_000 });

            var confirmationLink = await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ConfirmEmail", TimeSpan.FromSeconds(20));
            await page.GotoAsync(confirmationLink);
            await page.GetByText("Thank you for confirming your email").WaitForAsync(new() { Timeout = 10_000 });

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.DbConnectionString)
                .Options;
            await using var dbContext = new ApplicationDbContext(options);

            var user = await dbContext.Users.SingleAsync(u => u.Email == email);
            Assert.True(user.EmailConfirmed);
            Assert.Equal("555-0123", user.PhoneNumber);

            var userRoleIds = await dbContext.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync();
            var roleNames = await dbContext.Roles.Where(r => userRoleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync();
            Assert.Contains(StorageService.StorageCustomerRoleName, roleNames);

            var profile = await dbContext.StorageCustomerProfiles.SingleAsync(p => p.UserId == user.Id);
            Assert.Equal("456 Harbor Way", profile.AddressLine1);
            Assert.Equal("Bakersfield", profile.City);
            Assert.Equal("CA", profile.State);
            Assert.Equal("93301", profile.PostalCode);
            Assert.Equal("Sam Backup", profile.EmergencyContactName);
            Assert.Equal("555-0199", profile.EmergencyContactPhone);
        }
    }
}
