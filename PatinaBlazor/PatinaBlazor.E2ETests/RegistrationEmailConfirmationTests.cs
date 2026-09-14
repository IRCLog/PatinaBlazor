using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PatinaBlazor.Data;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Exercises the real Register -> send email -> click link -> confirmed flow end to end,
    // through Mailpit rather than RegisterConfirmation.razor's own "Development Mode" direct
    // link. That distinction matters: the dev-mode link is generated independently, inline on
    // that page, from its own fresh GenerateEmailConfirmationTokenAsync call - it never goes
    // through EmailTemplateRenderer/IdentitySmtpEmailSender at all, so it would never have
    // caught the real double-encoding bug this app shipped once in production (see CLAUDE.md -
    // a manually-pre-encoded callback URL got encoded a second time by the Razor email
    // template, corrupting the "&" between query parameters). Only the email Mailpit actually
    // received went through that real path, so only reading it back proves the real thing
    // works.
    [Collection("E2E")]
    public class RegistrationEmailConfirmationTests
    {
        private readonly WebAppFixture _fixture;

        public RegistrationEmailConfirmationTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Register_ThenClickRealEmailedLink_ConfirmsTheAccount()
        {
            var email = $"e2e-register-{Guid.NewGuid():N}@example.com";
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

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.DbConnectionString)
                .Options;
            await using var dbContext = new ApplicationDbContext(options);
            var user = await dbContext.Users.SingleAsync(u => u.Email == email);
            Assert.True(user.EmailConfirmed);
        }
    }
}
