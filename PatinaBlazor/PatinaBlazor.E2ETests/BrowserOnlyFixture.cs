using Microsoft.Playwright;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // A lightweight sibling to WebAppFixture, for tests that need a real browser but nothing
    // else - no SQL Server container, no Mailpit, no app subprocess. Exists specifically for
    // testing paypalCardFields.js's own logic (_describeError's parsing of PayPal's various
    // real error shapes) in a real browser JS engine, the same way this session's throwaway
    // Playwright harnesses did by hand repeatedly while debugging - except permanent, per the
    // user's explicit request and this project's standing "no throwaway-only verification"
    // discipline. Sharing WebAppFixture's "E2E" collection would work but would pay for a SQL/
    // Mailpit container start on every run even though these tests never touch either.
    public class BrowserOnlyFixture : IAsyncLifetime
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public async Task<IPage> NewPageAsync() => await _browser!.NewPageAsync();

        public async Task InitializeAsync()
        {
            var installExitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (installExitCode != 0)
            {
                throw new InvalidOperationException($"Playwright browser install failed with exit code {installExitCode}.");
            }

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }

        public async Task DisposeAsync()
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
            }
            _playwright?.Dispose();
        }
    }

    [CollectionDefinition("BrowserOnly")]
    public class BrowserOnlyCollection : ICollectionFixture<BrowserOnlyFixture>
    {
    }
}
