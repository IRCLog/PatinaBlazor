using Microsoft.Playwright;
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

        [Fact]
        public async Task AdminDashboard_AnonymousUser_RedirectsToLogin()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/admin");

            Assert.Contains("/Account/Login", page.Url);
        }

        // Regression guard for the exact bug this dashboard was rebuilt to fix - Admin.razor
        // used to be the untouched dotnet-new-blazor scaffold template, showing hardcoded fake
        // numbers ("Total Users: 1,234", a mock "Recent Activity" table with john.doe@
        // example.com) that were never wired to anything real. This test doesn't just check
        // the page renders - it asserts real, independently-known-correct values (nothing
        // seeds Collectables or Articles in this fixture, so those counts must be exactly
        // zero) and that the specific old fake strings can never silently reappear.
        [Fact]
        public async Task AdminDashboard_LoggedInAsDevTestAdmin_RendersRealData()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AdminEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/admin");

            await page.GetByText("Total Users").WaitForAsync(new() { Timeout = 10_000 });
            var bodyText = await page.InnerTextAsync("body");

            // The scaffold's old hardcoded values must never come back.
            Assert.DoesNotContain("1,234", bodyText);
            Assert.DoesNotContain("john.doe@example.com", bodyText);
            Assert.DoesNotContain("Active Sessions", bodyText);

            // Deterministic: this fixture's seeding never creates a Collectable or an
            // Article, so these two stat cards must read exactly zero.
            Assert.Contains("Collectables\n\n0", bodyText);
            Assert.Contains("Published Articles\n\n0\n0 total", bodyText);

            // Real Quick Actions, including the one added in this rebuild.
            await page.GetByRole(AriaRole.Link, new() { Name = "Manage Articles" }).WaitForAsync();

            // The old fake buttons that never did anything must be gone. Case-insensitive:
            // MudButton renders its label through a CSS text-transform: uppercase, and
            // Chromium's innerText (unlike a raw DOM textContent read) reflects that computed
            // style - real rendered text here is "CLEAR CACHE", not "Clear Cache".
            Assert.DoesNotContain("Clear Cache", bodyText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System Maintenance", bodyText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AdminDashboard_LoggedInAsNonAdminDevTestAccount_IsForbidden()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AutomationEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/admin");

            var heading = page.GetByText("Admin Panel");
            await Task.Delay(1000);
            Assert.Equal(0, await heading.CountAsync());
        }
    }
}
