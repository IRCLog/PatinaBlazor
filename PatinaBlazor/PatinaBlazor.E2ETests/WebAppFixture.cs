using System.Diagnostics;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Playwright;
using Testcontainers.MsSql;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Runs the real, compiled app as a subprocess (real Kestrel, real SignalR/Blazor Server
    // circuits) against a real, disposable SQL Server container - the only way to catch the
    // class of bug Tier 1 (PatinaBlazor.Tests) structurally cannot: the search-box circuit
    // crash, the mobile drawer/z-index issues, the email double-encoding bug, anything that
    // only manifests in an actual browser talking to an actual running server. Shared across
    // the whole "E2E" collection (starting the container, launching the app, and installing/
    // launching a browser are all too slow to redo per test) - each test gets its own
    // IBrowserContext from the one shared browser for cookie/storage isolation.
    //
    // The app's own Program.cs already runs EF migrations and DatabaseSeeder at startup
    // unconditionally, before it starts accepting connections - so this fixture does not
    // duplicate that logic (unlike Tier 1's DatabaseFixture, which has no running app to do
    // it for them). Polling the app's root URL until it responds is sufficient to know
    // migrations/seeding have already completed, since Program.cs's startup block runs
    // before app.Run() ever lets Kestrel accept a request.
    //
    // Also starts a throwaway Mailpit container (a fake SMTP server) so flows requiring a
    // real email round-trip - registration's confirmation link, password reset - can be
    // tested end to end: the launched app is configured to send mail to it via environment
    // variable overrides, and MailpitClient (see that file) fetches captured messages back
    // out through Mailpit's REST API to extract the link a real user would click.
    public class WebAppFixture : IAsyncLifetime
    {
        private MsSqlContainer? _dbContainer;
        private IContainer? _mailpitContainer;
        private Process? _appProcess;
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public string BaseUrl { get; private set; } = "";

        // The DB connection string is exposed so tests can make direct-DB assertions
        // (e.g. "did EmailConfirmed actually flip to true") alongside the browser-visible
        // outcome, the same level of rigor Tier 1 applies - not just trusting rendered text.
        public string DbConnectionString => _dbContainer!.GetConnectionString();

        // Mailpit's HTTP API base URL - used by MailpitClient to fetch captured emails and
        // extract confirmation/reset links, for flows Playwright can't complete on its own
        // (it can submit the form that triggers an email, but it can't "receive" one).
        public string MailpitApiBaseUrl { get; private set; } = "";

        public MailpitClient Mailpit => new(MailpitApiBaseUrl);

        public async Task<IBrowserContext> NewContextAsync() => await _browser!.NewContextAsync();

        public async Task InitializeAsync()
        {
            _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword("Test-P@ssw0rd-2026!")
                .Build();

            // A throwaway Mailpit per test run, matching the SQL container's disposable
            // philosophy - avoids a "find the latest email to X" query ever being confused by
            // leftover messages from an earlier run against the shared persistent dev Mailpit.
            _mailpitContainer = new ContainerBuilder("axllent/mailpit:v1.31.1")
                .WithPortBinding(1025, true)
                .WithPortBinding(8025, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/messages")))
                .Build();

            await Task.WhenAll(_dbContainer.StartAsync(), _mailpitContainer.StartAsync());

            var mailpitSmtpPort = _mailpitContainer.GetMappedPublicPort(1025);
            var mailpitHttpPort = _mailpitContainer.GetMappedPublicPort(8025);
            MailpitApiBaseUrl = $"http://127.0.0.1:{mailpitHttpPort}";

            var port = GetFreeTcpPort();
            BaseUrl = $"http://127.0.0.1:{port}";

            var (appDllPath, contentRootPath) = LocateApp();

            _appProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    ArgumentList = { appDllPath, "--contentroot", contentRootPath },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    EnvironmentVariables =
                    {
                        ["ASPNETCORE_ENVIRONMENT"] = "Development",
                        ["ASPNETCORE_URLS"] = BaseUrl,
                        ["ConnectionStrings__SqlServerConnection"] = _dbContainer.GetConnectionString(),
                        // Mailpit needs no real SMTP credentials - SmtpEmailSender still
                        // requires non-empty values, so placeholders are used, same as this
                        // app's own documented local-dev Mailpit setup.
                        ["EmailSettings__SmtpHost"] = "127.0.0.1",
                        ["EmailSettings__SmtpPort"] = mailpitSmtpPort.ToString(),
                        ["EmailSettings__EnableSsl"] = "false",
                        ["EmailSettings__SmtpUser"] = "e2etest",
                        ["EmailSettings__SmtpPassword"] = "e2etest",
                        ["EmailSettings__FromEmail"] = "noreply@e2etest.local",
                        ["EmailSettings__FromName"] = "PatinaBlazor E2E Test",
                        // Deliberately invalid, not absent - ASP.NET Core would otherwise
                        // auto-load this dev machine's real PayPal sandbox user-secrets
                        // (Development environment + a UserSecretsId baked into the
                        // assembly), making these tests pass locally but fail in CI (no
                        // secrets there) or vice versa. Forcing a real, deterministic PayPal
                        // auth failure here is intentional - Tier 2 only tests that the app
                        // degrades gracefully when PayPal rejects a call, never the real
                        // approval flow itself (that needs a human in a real browser; see
                        // the 2026-09-16 checkpoint entry's manual sandbox verification).
                        ["Paypal__ClientId"] = "e2e-test-invalid-client-id",
                        ["Paypal__ClientSecret"] = "e2e-test-invalid-client-secret"
                    }
                }
            };
            _appProcess.Start();

            await WaitForAppToBeReadyAsync();

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

            if (_appProcess is { HasExited: false })
            {
                _appProcess.Kill(entireProcessTree: true);
                await _appProcess.WaitForExitAsync();
            }
            _appProcess?.Dispose();

            if (_dbContainer != null)
            {
                await _dbContainer.DisposeAsync();
            }
            if (_mailpitContainer != null)
            {
                await _mailpitContainer.DisposeAsync();
            }
        }

        private async Task WaitForAppToBeReadyAsync()
        {
            using var client = new HttpClient();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            Exception? lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                if (_appProcess!.HasExited)
                {
                    var stdout = await _appProcess.StandardOutput.ReadToEndAsync();
                    var stderr = await _appProcess.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException(
                        $"App process exited early with code {_appProcess.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
                }

                try
                {
                    var response = await client.GetAsync(BaseUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(500);
            }

            throw new TimeoutException($"App at {BaseUrl} did not become ready in time.", lastError);
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // Walks up from the test assembly's own output directory to find the solution root
        // (identified by PatinaBlazor.sln), then locates the main app's own build output and
        // project folder from there - rather than relying on whatever a ProjectReference
        // happens to copy into this test project's output directory, which is not guaranteed
        // to include static web assets like wwwroot.
        private static (string AppDllPath, string ContentRootPath) LocateApp()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PatinaBlazor.sln")))
            {
                dir = dir.Parent;
            }
            if (dir is null)
            {
                throw new InvalidOperationException("Could not locate PatinaBlazor.sln above the test output directory.");
            }

            var contentRootPath = Path.Combine(dir.FullName, "PatinaBlazor");
            var appDllPath = Path.Combine(contentRootPath, "bin", "Debug", "net10.0", "PatinaBlazor.dll");
            if (!File.Exists(appDllPath))
            {
                throw new InvalidOperationException($"Expected app build output at '{appDllPath}' - build PatinaBlazor.csproj first.");
            }

            return (appDllPath, contentRootPath);
        }
    }

    [CollectionDefinition("E2E")]
    public class E2ECollection : ICollectionFixture<WebAppFixture>
    {
    }
}
