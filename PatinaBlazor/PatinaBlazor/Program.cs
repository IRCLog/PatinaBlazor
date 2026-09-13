using System.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PatinaBlazor.Components;
using PatinaBlazor.Components.Account;
using PatinaBlazor.Data;
using PatinaBlazor.Endpoints;
using PatinaBlazor.Hubs;
using PatinaBlazor.Interceptors;
using PatinaBlazor.Services;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using App = PatinaBlazor.Components.App;

var builder = WebApplication.CreateBuilder(args);

// Use SQL Server for all environments. Read up-front (rather than where this line lived
// previously, further down) so both EF and Serilog's MSSqlServer sink share this exact
// connection string - one place, no duplicated secret.
var sqlServerConnectionString = builder.Configuration.GetConnectionString("SqlServerConnection") ?? throw new InvalidOperationException("Connection string 'SqlServerConnection' not found.");

// Serilog replaces the default logging provider entirely, so every existing ILogger<T>
// call already in this codebase (ImageService, ArticleService, etc.), plus the
// framework's own internal logging (unhandled-exception logging in
// ExceptionHandlerMiddleware, Blazor Server's circuit-host exception logging) starts
// flowing into the DB sink with zero other code changes. EntityType/EntityId are named
// message-template holes (see Interceptors/EntityLogicUnit.cs's LogXxx helpers) - Serilog
// captures those as structured properties automatically, and the matching AdditionalColumns
// entries below promote them into real columns on the Logs table. columnOptions.TimeStamp's
// ConvertToUtc keeps this table's storage consistent with every other timestamp column in
// the app (all UTC) - display code converts to Central at read time instead (see
// Data/AppLogEntry.cs's TimeStampCentral).
//
// The Logs table itself is entirely Serilog's to own (AutoCreateSqlTable = true) - it is
// deliberately excluded from EF's migrations (see ApplicationDbContext.OnModelCreating's
// ExcludeFromMigrations() call) so the two never fight over its schema.
var logColumnOptions = new ColumnOptions();
logColumnOptions.TimeStamp.ConvertToUtc = true;
logColumnOptions.AdditionalColumns = new List<SqlColumn>
{
    new() { ColumnName = "EntityType", PropertyName = "EntityType", DataType = SqlDbType.NVarChar, DataLength = 256, AllowNull = true },
    new() { ColumnName = "EntityId", PropertyName = "EntityId", DataType = SqlDbType.NVarChar, DataLength = 256, AllowNull = true },
    new() { ColumnName = "UserId", PropertyName = "UserId", DataType = SqlDbType.NVarChar, DataLength = 450, AllowNull = true },
    new() { ColumnName = "EventCategory", PropertyName = "EventCategory", DataType = SqlDbType.NVarChar, DataLength = 100, AllowNull = true },
};

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        // Information is the permissive floor here - the actual gate is the Filter below.
        // Serilog's LogEventLevel is a fixed, non-extensible enum (no room for a level
        // between Information and Error), so "log Error and above, plus a Security
        // category regardless of its own level" is expressed as a filter predicate instead
        // of a level: drop anything below Error unless it carries an EventCategory=Security
        // property (see Services/SecurityLoggerExtensions.cs's LogSecurityInformation/
        // LogSecurityWarning, used for account events - logins, registrations, password
        // resets, email confirmations). Everything else (routine LogicUnit/framework
        // Information/Warning noise) stays suppressed exactly as before.
        .MinimumLevel.Information()
        .Filter.ByExcluding(evt =>
            evt.Level < LogEventLevel.Error &&
            !(evt.Properties.TryGetValue("EventCategory", out var category) && category is ScalarValue { Value: "Security" }))
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.MSSqlServer(
            connectionString: sqlServerConnectionString,
            sinkOptions: new MSSqlServerSinkOptions { TableName = "Logs", AutoCreateSqlTable = true },
            columnOptions: logColumnOptions);
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices(config =>
{
    // MudProviders is rendered as its own interactive island inside MainLayout,
    // alongside other independent render-mode islands (AppShell, page content).
    // MudBlazor's duplicate-provider guard fires as a false positive in this
    // multi-island topology even though only one MudPopoverProvider is declared
    // (see Components/Layout/MudProviders.razor) — disable the guard rather
    // than the check finding a real second instance.
    config.PopoverOptions.ThrowOnDuplicateProvider = false;
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, PersistingRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// ImageService has no per-request state (just IWebHostEnvironment/ILogger, both
// singleton-safe) - registered as a singleton so logic units resolved through
// EntityLogicUnitInterceptor's per-save scope (see Interceptors/) can depend on it
// without hitting the "cannot consume scoped service from singleton" DI validation error.
builder.Services.AddSingleton<IImageService, ImageService>();
builder.Services.AddSingleton<EntityLogicUnitInterceptor>();

// Blazor Server keeps one DI scope (and one scoped ApplicationDbContext) alive for a
// circuit's entire lifetime, not per page - so a still-in-flight query from a page the
// user just left can race a query the next page fires immediately on navigation, since
// EF Core's DbContext isn't safe for concurrent use. Registering AddDbContextFactory
// (rather than AddDbContext) and deriving the scoped ApplicationDbContext from it keeps
// every existing @inject ApplicationDbContext consumer working unchanged, while also
// making IDbContextFactory<ApplicationDbContext> available for services (like
// ArticleService) that create a short-lived, per-call context instead.
//
// EntityLogicUnitInterceptor is registered here so any EntityLogicUnit<T> (e.g.
// ImageCleanupLogicUnit, which cleans up ISupportImageAttachments photo files on delete)
// gets discovered and dispatched to automatically on every save, regardless of which
// service/page triggers it - see Interceptors/EntityLogicUnitInterceptor.cs for details.
builder.Services.AddDbContextFactory<ApplicationDbContext>((serviceProvider, options) =>
    options.UseSqlServer(sqlServerConnectionString)
           .AddInterceptors(serviceProvider.GetRequiredService<EntityLogicUnitInterceptor>()));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options => 
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Configure email settings
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// Configure IRC API settings
builder.Services.Configure<IrcApiSettings>(builder.Configuration.GetSection("IrcApi"));

// Register email services
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddTransient<IEmailSender<ApplicationUser>, IdentitySmtpEmailSender>();
builder.Services.AddSingleton<EmailTemplateRenderer>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<ImageAttachmentMigrationService>();
builder.Services.AddScoped<ICollectableService, CollectableService>();
builder.Services.AddScoped<ICollectionService, CollectionService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IArticleService, ArticleService>();
builder.Services.AddSingleton<IrcChatNotifier>();
builder.Services.AddSingleton<IrcBotService>();
builder.Services.AddScoped<IIrcEventService, IrcEventService>();
builder.Services.AddHostedService<IrcBotHeartbeatService>();
builder.Services.AddSignalR();

// Configure request size limits for file uploads (15MB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15MB
    options.ValueLengthLimit = 15 * 1024 * 1024; // 15MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 15 * 1024 * 1024; // 15MB
});

var app = builder.Build();

// Apply pending database migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Apply database migrations for SQL Server
        logger.LogInformation("Applying database migrations...");
        await context.Database.MigrateAsync();

        // Convert any pre-unification CollectableImages/StoragePropertyImages rows into
        // ImageAttachments (re-processed through IImageService) and drop those legacy tables
        // once every row converts successfully. No-op once both tables are gone.
        logger.LogInformation("Checking for legacy image tables to migrate...");
        var imageMigration = scope.ServiceProvider.GetRequiredService<ImageAttachmentMigrationService>();
        await imageMigration.MigrateLegacyImagesAsync();

        // Seed the database with default user
        logger.LogInformation("Starting database seeding...");
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
        logger.LogInformation("Database setup completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database setup failed: {Message}", ex.Message);
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Logs every request (path, status code, timing) through the same Serilog pipeline as
// everything else - placed early so it wraps the full pipeline, including error responses
// from UseExceptionHandler above.
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

// MapStaticAssets (introduced in .NET 9) is required for the framework's own build-time static
// assets (notably _framework/blazor.web.js when both Server and WebAssembly render modes are
// registered) to resolve correctly; UseStaticFiles alone 404s on blazor.web.js under .NET 10,
// silently breaking every interactive circuit on the page.
//
// UseStaticFiles is restored alongside it as a safety net for wwwroot/uploads/ - files written at
// runtime by ImageService.SaveImageAsync, never present in the build-time static-web-assets
// manifest MapStaticAssets serves from. On plain Kestrel, MapStaticAssets alone was observed to
// still serve those files fine (fell back to disk), so this wasn't reproduced as the cause of a
// reported production image-loading regression - restoring UseStaticFiles is cheap insurance for
// any hosting-specific difference (e.g. IIS) rather than a confirmed root-cause fix.
app.MapStaticAssets();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Add IRC event API endpoints
app.MapIrcEventEndpoints();
app.MapHub<IrcBotHub>("/hubs/ircbot");

try
{
    app.Run();
}
finally
{
    // Flushes the MSSqlServer sink's batched writes on graceful shutdown.
    Log.CloseAndFlush();
}
