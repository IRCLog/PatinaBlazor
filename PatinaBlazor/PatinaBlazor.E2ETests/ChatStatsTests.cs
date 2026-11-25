using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    [Collection("E2E")]
    public class ChatStatsTests
    {
        private readonly WebAppFixture _fixture;

        public ChatStatsTests(WebAppFixture fixture) => _fixture = fixture;

        [Fact]
        public async Task ChatStats_AnonymousUser_RedirectsToLogin()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/chat/stats");

            Assert.Contains("/Account/Login", page.Url);
        }

        [Fact]
        public async Task ChatStats_LoggedInAsNonAdmin_IsForbidden()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AutomationEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/chat/stats");

            await Task.Delay(1000);
            Assert.Equal(0, await page.GetByText("IRC Stats").CountAsync());
        }

        [Fact]
        public async Task ChatStats_LoggedInAsAdmin_ShowsCountsForSeededEvents()
        {
            var network = "e2e-" + Guid.NewGuid().ToString("N")[..8];
            var now = DateTime.UtcNow.AddMinutes(-5);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_fixture.DbConnectionString).Options;
            await using (var db = new ApplicationDbContext(options))
            {
                db.IrcEvents.AddRange(
                    new IrcEvent { Network = network, Action = ChatAction.MESSAGE, Channel = "#e2e", Sender = "alice", Timestamp = now },
                    new IrcEvent { Network = network, Action = ChatAction.MESSAGE, Channel = "#e2e", Sender = "bob", Timestamp = now },
                    new IrcEvent { Network = network, Action = ChatAction.JOIN, Channel = "#e2e", Sender = "bob", Timestamp = now });
                await db.SaveChangesAsync();
            }

            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AdminEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/chat/stats");

            await page.GetByLabel("Network").ClickAsync();
            await page.GetByRole(AriaRole.Option, new() { Name = network }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();

            await Assertions.Expect(page.Locator("#stat-events")).ToHaveTextAsync("3", new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator("#stat-messages")).ToHaveTextAsync("2");
            await Assertions.Expect(page.Locator("#stat-users")).ToHaveTextAsync("2");
            await Assertions.Expect(page.Locator("#stat-channels")).ToHaveTextAsync("1");
        }

        [Fact]
        public async Task NavMenu_ChatGroup_ExposesLiveChatAndStatsLinks()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AdminEmail, DevTestAccounts.Password);
            await page.GotoAsync(_fixture.BaseUrl);

            await page.GetByText("Chat", new() { Exact = true }).First.ClickAsync();

            await Assertions.Expect(page.GetByText("Live Chat", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByText("Stats", new() { Exact = true }).ClickAsync();
            await page.WaitForURLAsync(url => url.EndsWith("/chat/stats"), new() { Timeout = 10_000 });
        }
    }
}
