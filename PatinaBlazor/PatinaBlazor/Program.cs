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
using PatinaBlazor.Services;
using App = PatinaBlazor.Components.App;

var builder = WebApplication.CreateBuilder(args);

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

// Use SQL Server for all environments
var sqlServerConnectionString = builder.Configuration.GetConnectionString("SqlServerConnection") ?? throw new InvalidOperationException("Connection string 'SqlServerConnection' not found.");

// Blazor Server keeps one DI scope (and one scoped ApplicationDbContext) alive for a
// circuit's entire lifetime, not per page - so a still-in-flight query from a page the
// user just left can race a query the next page fires immediately on navigation, since
// EF Core's DbContext isn't safe for concurrent use. Registering AddDbContextFactory
// (rather than AddDbContext) and deriving the scoped ApplicationDbContext from it keeps
// every existing @inject ApplicationDbContext consumer working unchanged, while also
// making IDbContextFactory<ApplicationDbContext> available for services (like
// ArticleService) that create a short-lived, per-call context instead.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(sqlServerConnectionString));
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
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<ImageAttachmentMigrationService>();
builder.Services.AddScoped<IImageService, ImageService>();
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

app.UseHttpsRedirection();

// MapStaticAssets (introduced in .NET 9) replaces UseStaticFiles for Razor Components apps -
// required for the framework's own static assets (notably _framework/blazor.web.js when both
// Server and WebAssembly render modes are registered) to resolve correctly; UseStaticFiles alone
// 404s on blazor.web.js under .NET 10, silently breaking every interactive circuit on the page.
app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Add IRC event API endpoints
app.MapIrcEventEndpoints();
app.MapHub<IrcBotHub>("/hubs/ircbot");

app.Run();
