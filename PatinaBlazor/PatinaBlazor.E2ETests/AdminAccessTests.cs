using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    [Collection("E2E")]
    public class AdminAccessTests
    {
        private readonly WebAppFixture _fixture;

        public AdminAccessTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task AdminLogsPage_AnonymousUser_RedirectsToLogin()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            var response = await page.GotoAsync($"{_fixture.BaseUrl}/admin/logs");

            Assert.Contains("/Account/Login", page.Url);
        }

        [Fact]
        public async Task AdminLogsPage_LoggedInAsDevTestAdmin_RendersSuccessfully()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AdminEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/admin/logs");

            await page.GetByText("Application Logs").WaitForAsync(new() { Timeout = 10_000 });
        }

        [Fact]
        public async Task AdminLogsPage_LoggedInAsNonAdminDevTestAccount_IsForbidden()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AutomationEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/admin/logs");

            // Not the "Application Logs" heading - MudBlazor's [Authorize] fallback UI
            // (AuthorizeRouteView's NotAuthorized content) should render instead.
            var heading = page.GetByText("Application Logs");
            await Task.Delay(1000); // let the page settle before asserting a negative
            Assert.Equal(0, await heading.CountAsync());
        }
    }
}
