using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Exercises the real /storage/signup 4-step wizard end to end: account info, live
    // email verification (via the real Mailpit-delivered link, clicked in a *separate*
    // browser tab - proving the original wizard tab advances on its own over the
    // EmailConfirmationNotifier/SignalR push, not a page reload), unit picking/reservation,
    // and the Phase 1 payment placeholder. Verifies against the real DB throughout, not
    // just what the UI displays.
    [Collection("E2E")]
    public class StorageCustomerSignUpTests
    {
        private readonly WebAppFixture _fixture;

        public StorageCustomerSignUpTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task FullWizard_AccountInfoThroughUnitReservation_AdvancesLiveAndPersistsCorrectly()
        {
            var email = $"e2e-storagesignup-{Guid.NewGuid():N}@example.com";
            const string password = "E2eTest123!";

            await using var context = await _fixture.NewContextAsync();
            var wizardPage = await context.NewPageAsync();

            await wizardPage.GotoAsync($"{_fixture.BaseUrl}/storage/signup");
            await wizardPage.GetByPlaceholder("name@example.com").FillAsync(email);
            await wizardPage.GetByPlaceholder("First name").FillAsync("Riley");
            await wizardPage.GetByPlaceholder("Last name").FillAsync($"E2E{Guid.NewGuid():N}"[..10]);
            await wizardPage.GetByPlaceholder("password", new() { Exact = true }).FillAsync(password);
            await wizardPage.GetByPlaceholder("confirm password").FillAsync(password);
            await wizardPage.GetByPlaceholder("Phone number").FillAsync("555-0123");
            await wizardPage.GetByPlaceholder("Address line 1").FillAsync("456 Harbor Way");
            await wizardPage.GetByPlaceholder("City").FillAsync("Bakersfield");
            await wizardPage.GetByPlaceholder("State").FillAsync("CA");
            await wizardPage.GetByPlaceholder("Postal code").FillAsync("93301");
            await wizardPage.GetByPlaceholder("Emergency contact name").FillAsync("Sam Backup");
            await wizardPage.GetByPlaceholder("Emergency contact phone").FillAsync("555-0199");
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

            // Step 2: still the same page/circuit, no navigation - the wizard advances itself
            // client-side the instant registration succeeds.
            await wizardPage.GetByText("Check your email").WaitForAsync(new() { Timeout = 10_000 });

            var confirmationLink = await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ConfirmEmail", TimeSpan.FromSeconds(20));

            // Deliberately a *second* tab, mirroring how a real customer clicks the link from
            // their email client rather than the tab the wizard is open in - this is the real
            // test of the live EmailConfirmationNotifier push, not just that confirmation works.
            var confirmPage = await context.NewPageAsync();
            await confirmPage.GotoAsync(confirmationLink);
            await confirmPage.GetByText("Thank you for confirming your email").WaitForAsync(new() { Timeout = 10_000 });
            await confirmPage.CloseAsync();

            // Back on the original wizard tab, with no reload of any kind - it should have
            // advanced to Step 3 on its own the moment the confirmation above completed.
            await wizardPage.GetByText("Pick a Unit", new() { Exact = false }).First.WaitForAsync(new() { Timeout = 10_000 });
            var selectButton = wizardPage.GetByRole(AriaRole.Button, new() { Name = "Select" }).First;
            await selectButton.WaitForAsync(new() { Timeout = 10_000 });
            await selectButton.ClickAsync();

            // Step 4 - the real billing-frequency picker (Part 2), not a placeholder anymore.
            await wizardPage.GetByText("Billing Frequency").WaitForAsync(new() { Timeout = 10_000 });

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

            var rental = await dbContext.StorageRentals
                .Include(r => r.Unit)
                .SingleAsync(r => r.CustomerUserId == user.Id);
            Assert.Equal(StorageRentalStatus.PendingPayment, rental.Status);
            Assert.NotNull(rental.Unit);
            Assert.Equal(StorageUnitStatus.Reserved, rental.Unit!.Status);
            Assert.Equal(rental.Unit.MonthlyRate, rental.MonthlyRateAtSigning);

            // The Payment step's summary text should name the exact unit that got reserved -
            // ties the UI's own displayed state back to the real DB row, not just "a rental
            // exists somewhere."
            await wizardPage.GetByText($"Unit {rental.Unit.UnitNumber}").WaitForAsync(new() { Timeout = 5_000 });
        }
    }
}
