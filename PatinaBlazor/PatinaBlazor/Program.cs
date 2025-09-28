using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Components;
using PatinaBlazor.Components.Account;
using PatinaBlazor.Data;
using PatinaBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(connectionString));
}
else
{
    var sqlServerConnectionString = builder.Configuration.GetConnectionString("SqlServerConnection") ?? connectionString;
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(sqlServerConnectionString));
}
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

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<IImageService, ImageService>();

var app = builder.Build();

// Apply pending database migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // For SQL Server, try to fix key length issues before migration
        if (context.Database.IsSqlServer())
        {
            logger.LogInformation("Detected SQL Server - checking for key length issues...");

            try
            {
                // Test if we can access the database and run migrations
                logger.LogInformation("Testing database connection and applying migrations...");
                await context.Database.MigrateAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("invalid for use as a key column"))
            {
                logger.LogWarning("Key column length issue detected. Recreating database schema...");

                // Drop all tables and recreate with correct schema
                logger.LogInformation("Dropping all tables to fix key length issues...");

                // Get all table names and drop them
                var tableNames = new[]
                {
                    "AspNetUserTokens", "AspNetUserRoles", "AspNetUserLogins", "AspNetUserClaims",
                    "AspNetRoleClaims", "CollectableImages", "Collectables", "HitCounters",
                    "AspNetUsers", "AspNetRoles", "__EFMigrationsHistory"
                };

                foreach (var tableName in tableNames)
                {
                    try
                    {
                        await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS [{tableName}]");
                        logger.LogInformation("Dropped table: {TableName}", tableName);
                    }
                    catch (Exception dropEx)
                    {
                        logger.LogWarning("Could not drop table {TableName}: {Error}", tableName, dropEx.Message);
                    }
                }

                logger.LogInformation("Recreating database schema with correct key lengths...");
                await context.Database.MigrateAsync();
            }
        }
        else
        {
            // SQLite - normal migration
            await context.Database.MigrateAsync();
        }

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

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
