using System.Diagnostics;
using System.Net.Sockets;
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
    public class WebAppFixture : IAsyncLifetime
    {
        private MsSqlContainer? _dbContainer;
        private Process? _appProcess;
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public string BaseUrl { get; private set; } = "";

        public async Task<IBrowserContext> NewContextAsync() => await _browser!.NewContextAsync();

        public async Task InitializeAsync()
        {
            _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword("Test-P@ssw0rd-2026!")
                .Build();
            await _dbContainer.StartAsync();

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
                        ["ConnectionStrings__SqlServerConnection"] = _dbContainer.GetConnectionString()
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
