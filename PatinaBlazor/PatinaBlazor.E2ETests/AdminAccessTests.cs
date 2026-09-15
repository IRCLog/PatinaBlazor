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

            // Real root cause, confirmed by direct SQL polling during a failing run (not
            // guessed): the login's Security log row is written asynchronously - Serilog's
            // MSSqlServer sink batches writes (BatchPeriod defaults to 5s, though in practice
            // this row was already committed within ~1s in the observed failure) - and
            // Admin.razor's OnInitializedAsync queries Recent Activity exactly once, when the
            // page first loads, with no live refresh afterward. If the browser's navigation to
            // /admin (immediately after the login redirect) reaches the server before that
            // async write has landed, the page renders a snapshot that will never include the
            // row, no matter how long the test then waits on that same static render - the
            // page itself never re-queries. A short delay wasn't enough on its own; a reload
            // forces a genuinely fresh query, taken after the write has had time to land.
            await Task.Delay(2000);
            await page.ReloadAsync();

            // .First - other tests logging in earlier in the suite mean more than one
            // "logged in" row can legitimately exist by now (confirmed via the real error this
            // produced without it: "strict mode violation: ... resolved to 5 elements" -
            // Playwright's Locator actions require exactly one match unless narrowed). Recent
            // Activity orders newest-first, and the reload above guarantees a fresh query
            // taken after this test's own just-now login, so the first match is always this
            // test's own row.
            var loginActivityRow = page.Locator("table tr", new() { HasText = "logged in" }).First;
            await loginActivityRow.WaitForAsync(new() { Timeout = 10_000 });
            await Task.Delay(500);
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

            // Logging in just now produced a real "logged in" Security event for this exact
            // account, so its Recent Activity row's User column must show a resolved display
            // name, not the raw AspNetUsers.Id GUID - scoped to that specific row/cell (Time,
            // Level, User, Message in column order), not a body-wide substring search. Doesn't
            // pin the exact string (DevTestAccounts.AdminEmail's DisplayName, "Dev Test
            // (Admin)", is DatabaseSeeder's wording to own, not this test's) - the resolution
            // logic's exact fallback order is already precisely covered at the service layer
            // by AdminDashboardServiceTests; this only needs to prove a real resolved value
            // reaches the rendered page instead of the raw id.
            var userCellText = await loginActivityRow.Locator("td").Nth(2).InnerTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(userCellText));
            Assert.NotEqual("—", userCellText);
            Assert.DoesNotMatch(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", userCellText);
        }

        // Proves the auto-refresh added alongside the User column actually works, not just
        // that the page has a timer - a second, independent browser context triggers a new
        // Security event, and the first page (which is never reloaded or re-navigated) must
        // pick it up on its own within one refresh cycle. Before Admin.razor polled for
        // updates, this scenario was a real, if minor, product limitation: an admin watching
        // the dashboard wouldn't see a brand-new event appear without a manual refresh.
        [Fact]
        public async Task AdminDashboard_AutoRefreshesRecentActivityWithoutManualReload()
        {
            await using var adminContext = await _fixture.NewContextAsync();
            var adminPage = await adminContext.NewPageAsync();

            await adminPage.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AdminEmail, DevTestAccounts.Password);
            await adminPage.GotoAsync($"{_fixture.BaseUrl}/admin");
            await adminPage.GetByText("Total Users").WaitForAsync(new() { Timeout = 10_000 });

            // A uniquely-identifiable event, triggered from a completely separate browser
            // context so it's genuinely independent of adminPage's own session/navigation.
            var uniqueMarkerEmail = $"e2e-autorefresh-{Guid.NewGuid():N}@example.com";
            await using (var attackerContext = await _fixture.NewContextAsync())
            {
                var attackerPage = await attackerContext.NewPageAsync();
                await attackerPage.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
                await attackerPage.GetByPlaceholder("name@example.com").FillAsync(uniqueMarkerEmail);
                await attackerPage.GetByPlaceholder("password").FillAsync("WrongPassword123!");
                await attackerPage.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
                await attackerPage.GetByText("Invalid login attempt").WaitForAsync(new() { Timeout = 10_000 });
            }

            // No reload, no re-navigation - just wait past one refresh cycle (Admin.razor's
            // RefreshInterval is 15s; a generous margin above that absorbs normal test-run
            // scheduling variance without weakening what's actually being proved).
            var newRow = adminPage.Locator("table tr", new() { HasText = uniqueMarkerEmail });
            await newRow.WaitForAsync(new() { Timeout = 25_000 });

            Assert.Equal($"{_fixture.BaseUrl}/admin", adminPage.Url);
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
