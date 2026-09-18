using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PatinaBlazor.Data;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Confirms deactivating a user through the real Identity lockout mechanism
    // (UserDeactivationService, wired into AllUsers.razor's "delete" action per the user's
    // explicit direction that deleting a user should preserve their rentals/payment history
    // instead of destroying it) genuinely blocks sign-in through the real login page - not
    // just that the LockoutEnd column got set. A Tier 1 test already proves the mechanism via
    // UserManager.IsLockedOutAsync directly; this proves the actual SignInManager/login-page
    // stack respects it, which only a real browser-against-a-real-server round trip can show.
    [Collection("E2E")]
    public class UserDeactivationTests
    {
        private readonly WebAppFixture _fixture;

        public UserDeactivationTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeactivatedAccount_CannotSignInEvenWithTheCorrectPassword()
        {
            var email = $"e2e-deactivate-{Guid.NewGuid():N}@example.com";
            const string password = "E2eTest123!";

            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/Account/Register");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("YourDisplayName").FillAsync($"E2ETest{Guid.NewGuid():N}"[..20]);
            var passwordFields = page.GetByPlaceholder("password");
            await passwordFields.Nth(0).FillAsync(password);
            await passwordFields.Nth(1).FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("/Account/RegisterConfirmation"), new() { Timeout = 10_000 });

            var confirmationLink = await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ConfirmEmail", TimeSpan.FromSeconds(20));
            await page.GotoAsync(confirmationLink);
            await page.GetByText("Thank you for confirming your email").WaitForAsync(new() { Timeout = 10_000 });

            // Deactivating directly against the DB, mirroring UserDeactivationService's own
            // two writes - there's no in-process way to reach the app's DI container from this
            // test process, since the app under test runs as a real subprocess.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.DbConnectionString)
                .Options;
            await using (var dbContext = new ApplicationDbContext(options))
            {
                var user = await dbContext.Users.SingleAsync(u => u.Email == email);
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.MaxValue;
                await dbContext.SaveChangesAsync();
            }

            await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("password").FillAsync(password); // the real, correct password
            await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();

            await page.WaitForURLAsync(url => url.Contains("/Account/Lockout"), new() { Timeout = 10_000 });
            await page.GetByText("This account has been locked out").WaitForAsync(new() { Timeout = 5_000 });
        }
    }
}
