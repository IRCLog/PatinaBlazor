using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PatinaBlazor.Data;
using PatinaBlazor.Interceptors;
using PatinaBlazor.Services;
using Testcontainers.MsSql;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Spins up a real, disposable SQL Server container per test run (shared across every test
    // in the "Database" collection, not per-test - starting a container takes several seconds,
    // so one per run keeps the suite fast) via Testcontainers.MsSql. After the container is
    // ready, applies the app's real EF Core migrations and runs the app's real DatabaseSeeder,
    // so tests see the same roles/admin/dummy-customer/test-automation accounts a real dev DB
    // would have - not a hand-rolled subset that could drift from what SeedAsync actually does.
    public class DatabaseFixture : IAsyncLifetime
    {
        private MsSqlContainer? _container;
        private ServiceProvider? _serviceProvider;

        public IDbContextFactory<ApplicationDbContext> DbContextFactory =>
            _serviceProvider!.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        // WebRootPath for ImageService's file operations during tests - a temp directory
        // distinct from the real app's wwwroot, so tests never touch real uploaded files.
        public string WebRootPath { get; } = Path.Combine(Path.GetTempPath(), "PatinaBlazorTests_" + Guid.NewGuid());

        public IServiceScope CreateScope() => _serviceProvider!.CreateScope();

        public async Task InitializeAsync()
        {
            Directory.CreateDirectory(WebRootPath);

            // Pinned explicitly rather than relying on the library's own default (which is
            // itself marked obsolete, pushing consumers toward pinning) - avoids the test
            // suite silently picking up a different SQL Server image on a future
            // Testcontainers.MsSql upgrade.
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword("Test-P@ssw0rd-2026!")
                .Build();
            await _container.StartAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton<EntityLogicUnitInterceptor>();
            services.AddDbContextFactory<ApplicationDbContext>((sp, options) =>
                options.UseSqlServer(_container.GetConnectionString())
                       .AddInterceptors(sp.GetRequiredService<EntityLogicUnitInterceptor>()));
            var testEnvironment = new TestWebHostEnvironment(WebRootPath);
            services.AddSingleton<IWebHostEnvironment>(testEnvironment);
            services.AddSingleton<IHostEnvironment>(testEnvironment);
            services.AddSingleton<IImageService, ImageService>();
            services.AddScoped<ICollectableService, CollectableService>();
            services.AddScoped<ICollectionService, CollectionService>();
            services.AddScoped<IStorageService, StorageService>();
            services.AddScoped<IArticleService, ArticleService>();
            services.AddScoped<IAdminDashboardService, AdminDashboardService>();
            services.AddScoped<DatabaseSeeder>();
            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = true;
                    options.User.RequireUniqueEmail = true;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            _serviceProvider = services.BuildServiceProvider();

            await using (var context = await DbContextFactory.CreateDbContextAsync())
            {
                await context.Database.MigrateAsync();

                // The real app never creates this table via EF - Serilog's MSSqlServer sink
                // creates it itself at startup (AutoCreateSqlTable = true, see Program.cs) and
                // writes to it directly over ADO.NET, bypassing EF's change tracker entirely.
                // This test harness doesn't run that Serilog pipeline, so it creates the same
                // table by hand here - schema confirmed column-for-column against the real
                // table Serilog created in the actual dev DB (INFORMATION_SCHEMA.COLUMNS),
                // not guessed - so AdminDashboardServiceTests can insert real-shaped rows via
                // InsertLogEntryAsync below.
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE [Logs] (
                        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Message] nvarchar(max) NULL,
                        [MessageTemplate] nvarchar(max) NULL,
                        [Level] nvarchar(16) NULL,
                        [TimeStamp] datetime NULL,
                        [Exception] nvarchar(max) NULL,
                        [Properties] nvarchar(max) NULL,
                        [EntityType] nvarchar(256) NULL,
                        [EntityId] nvarchar(256) NULL,
                        [UserId] nvarchar(450) NULL,
                        [EventCategory] nvarchar(100) NULL
                    )
                    """);
            }

            using var scope = _serviceProvider.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync();
        }

        // Inserts a row shaped like a real Serilog MSSqlServer sink write - via raw SQL, since
        // AppLogEntry is mapped HasNoKey() and EF does not support inserting/tracking a keyless
        // entity type through SaveChanges (matching how the real app never writes this table
        // through EF either).
        public async Task InsertLogEntryAsync(string level, string message, DateTime timeStampUtc, string? eventCategory = null, string? userId = null)
        {
            await using var context = await DbContextFactory.CreateDbContextAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Logs] ([Message], [Level], [TimeStamp], [EventCategory], [UserId])
                VALUES ({message}, {level}, {timeStampUtc}, {eventCategory}, {userId})
                """);
        }

        public async Task DisposeAsync()
        {
            if (_serviceProvider != null)
            {
                await _serviceProvider.DisposeAsync();
            }
            if (_container != null)
            {
                await _container.DisposeAsync();
            }
            if (Directory.Exists(WebRootPath))
            {
                Directory.Delete(WebRootPath, recursive: true);
            }
        }
    }

    [CollectionDefinition("Database")]
    public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
    {
    }
}
