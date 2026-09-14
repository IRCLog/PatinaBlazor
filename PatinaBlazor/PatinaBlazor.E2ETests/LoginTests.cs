using Microsoft.Playwright;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    [Collection("E2E")]
    public class LoginTests
    {
        private readonly WebAppFixture _fixture;

        public LoginTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Login_WithCorrectCredentials_RedirectsAwayFromLoginPage()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
            await page.GetByPlaceholder("name@example.com").FillAsync(DevTestAccounts.AutomationEmail);
            await page.GetByPlaceholder("password").FillAsync(DevTestAccounts.Password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();

            await page.WaitForURLAsync(url => !url.Contains("/Account/Login"), new() { Timeout = 10_000 });
        }

        [Fact]
        public async Task Login_WithWrongPassword_ShowsInvalidLoginError()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
            await page.GetByPlaceholder("name@example.com").FillAsync(DevTestAccounts.AutomationEmail);
            await page.GetByPlaceholder("password").FillAsync("definitely-the-wrong-password");
            await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();

            await page.GetByText("Invalid login attempt").WaitForAsync(new() { Timeout = 10_000 });
            Assert.Contains("/Account/Login", page.Url);
        }
    }
}
