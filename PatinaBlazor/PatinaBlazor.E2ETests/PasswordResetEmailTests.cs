using Microsoft.Playwright;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Symmetrical to RegistrationEmailConfirmationTests - the password-reset flow the
    // 2026-09-13 Mailpit-in-WebAppFixture work explicitly queued as a follow-up (see
    // CLAUDE.md). Same reasoning applies here: ForgotPassword.razor/ResetPassword.razor's own
    // real EmailSender.SendPasswordResetLinkAsync call goes through the real
    // EmailTemplateRenderer/IdentitySmtpEmailSender pipeline - the exact pipeline that had the
    // double-encoding bug for ConfirmEmail once - so only a link fetched back out of a real
    // received email (via MailpitClient) actually exercises that path end to end.
    [Collection("E2E")]
    public class PasswordResetEmailTests
    {
        private readonly WebAppFixture _fixture;

        public PasswordResetEmailTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ForgotPassword_ThenClickRealEmailedLink_ResetsPasswordAndInvalidatesTheOldOne()
        {
            var email = $"e2e-resetpw-{Guid.NewGuid():N}@example.com";
            const string originalPassword = "E2eTest123!";
            const string newPassword = "E2eTestReset456!";

            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            // Register and confirm first - ForgotPassword.razor deliberately won't send a
            // reset email to an unconfirmed address (UserManager.IsEmailConfirmedAsync check),
            // matching this app's RequireConfirmedAccount setting.
            await page.GotoAsync($"{_fixture.BaseUrl}/Account/Register");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("YourDisplayName").FillAsync($"E2ETest{Guid.NewGuid():N}"[..20]);
            var registerPasswordFields = page.GetByPlaceholder("password");
            await registerPasswordFields.Nth(0).FillAsync(originalPassword);
            await registerPasswordFields.Nth(1).FillAsync(originalPassword);
            await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("/Account/RegisterConfirmation"), new() { Timeout = 10_000 });

            var confirmationLink = await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ConfirmEmail", TimeSpan.FromSeconds(20));
            await page.GotoAsync(confirmationLink);
            await page.GetByText("Thank you for confirming your email").WaitForAsync(new() { Timeout = 10_000 });

            // Request the reset, then fetch the real link out of the real received email.
            await page.GotoAsync($"{_fixture.BaseUrl}/Account/ForgotPassword");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByRole(AriaRole.Button, new() { Name = "Reset password" }).ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("/Account/ForgotPasswordConfirmation"), new() { Timeout = 10_000 });

            var resetLink = await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ResetPassword", TimeSpan.FromSeconds(20));

            await page.GotoAsync(resetLink);
            // ResetPassword.razor's callback URL only carries the reset code, not the email -
            // the form requires it to be re-entered.
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("Please enter your password.").FillAsync(newPassword);
            await page.GetByPlaceholder("Please confirm your password.").FillAsync(newPassword);
            await page.GetByRole(AriaRole.Button, new() { Name = "Reset", Exact = true }).ClickAsync();
            await page.GetByText("Your password has been reset").WaitForAsync(new() { Timeout = 10_000 });

            // The real proof, not just trusting the confirmation page (which is also shown for
            // the "unknown email" branch, so reaching it alone doesn't prove success): the new
            // password now logs in, and - just as importantly - the old one no longer does.
            await page.LoginAsync(_fixture.BaseUrl, email, newPassword);
            Assert.DoesNotContain("/Account/Login", page.Url);

            // Logout is a real POST form (antiforgery-protected, not a plain link) rendered in
            // the nav sidebar on every authenticated page - see NavMenu.razor.
            await page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("password").FillAsync(originalPassword);
            await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
            await page.GetByText("Invalid login attempt").WaitForAsync(new() { Timeout = 10_000 });
            Assert.Contains("/Account/Login", page.Url);
        }
    }
}
