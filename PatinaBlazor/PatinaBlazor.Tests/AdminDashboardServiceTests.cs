using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers AdminDashboardService - the aggregation logic backing Admin.razor's stat cards
    // and Recent Activity list, pulled out of the page's own code-behind specifically so it's
    // testable without a browser (see CLAUDE.md's 2026-09-14 /admin rebuild entry: the first
    // pass only verified this in a browser via a throwaway Playwright script, which left no
    // permanent coverage - this file is the fix for that).
    //
    // Counts are asserted as deltas from a baseline GetSummaryAsync() call, not as exact
    // numbers, since the shared "Database" collection's seeded data (dev accounts, dummy
    // Storage customers/properties) means the fixture never starts from a truly empty DB.
    [Collection("Database")]
    public class AdminDashboardServiceTests
    {
        private readonly DatabaseFixture _fixture;

        public AdminDashboardServiceTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetSummaryAsync_CountsReflectRealRows()
        {
            using var scope = _fixture.CreateScope();
            var dashboardService = scope.ServiceProvider.GetRequiredService<IAdminDashboardService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var articleService = scope.ServiceProvider.GetRequiredService<IArticleService>();

            var before = await dashboardService.GetSummaryAsync();

            var author = await userManager.FindByEmailAsync(DevTestAccounts.AutomationEmail);
            Assert.NotNull(author);

            // One new user (not via CreateThrowawayCollectable's plain insert - through the
            // real UserManager, matching how every real user is actually created).
            var newUserEmail = $"dashboardtest_{Guid.NewGuid():N}@patinablazor.local";
            var createResult = await userManager.CreateAsync(new ApplicationUser
            {
                UserName = newUserEmail,
                Email = newUserEmail,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            }, "ThrowawayTest123!");
            Assert.True(createResult.Succeeded);

            Guid collectableId;
            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var collectable = new Collectable
                {
                    Description = "Dashboard count test item",
                    PricePaid = 1.00m,
                    UserId = author!.Id
                };
                context.Collectables.Add(collectable);
                await context.SaveChangesAsync();
                collectableId = collectable.Id;
            }

            // One published, one draft - the published count must move by exactly one while
            // the total count moves by two.
            var published = await articleService.CreateAsync(new Article
            {
                Title = "Dashboard Count Test - Published",
                Body = "body",
                AuthorUserId = author.Id,
                Status = ArticleStatus.Published
            }, author.Id);
            var draft = await articleService.CreateAsync(new Article
            {
                Title = "Dashboard Count Test - Draft",
                Body = "body",
                AuthorUserId = author.Id,
                Status = ArticleStatus.Draft
            }, author.Id);

            var after = await dashboardService.GetSummaryAsync();

            Assert.Equal(before.UserCount + 1, after.UserCount);
            Assert.Equal(before.CollectableCount + 1, after.CollectableCount);
            Assert.Equal(before.TotalArticleCount + 2, after.TotalArticleCount);
            Assert.Equal(before.PublishedArticleCount + 1, after.PublishedArticleCount);

            // Cleanup - keep the shared DB's baseline stable for tests that run after this one.
            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                context.Collectables.RemoveRange(context.Collectables.Where(c => c.Id == collectableId));
                await context.SaveChangesAsync();
            }
            await articleService.DeleteAsync(published.Id);
            await articleService.DeleteAsync(draft.Id);
        }

        [Fact]
        public async Task GetSummaryAsync_ErrorCount24h_IncludesErrorsInsideTheWindowAndExcludesOutsideIt()
        {
            using var scope = _fixture.CreateScope();
            var dashboardService = scope.ServiceProvider.GetRequiredService<IAdminDashboardService>();

            // A fixed "now" so this test's pass/fail never depends on real wall-clock time or
            // races with other tests writing to the shared Logs table.
            var asOf = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

            // Exactly on the boundary (24h before asOf) - must be INCLUDED (the window is
            // inclusive at both ends, matching StorageRental.GetNextBillingDate's own
            // inclusive-boundary convention elsewhere in this codebase).
            await _fixture.InsertLogEntryAsync("Error", "boundary-inclusive-start", asOf.AddHours(-24));
            // One second before the boundary - must be EXCLUDED.
            await _fixture.InsertLogEntryAsync("Error", "boundary-exclusive-start", asOf.AddHours(-24).AddSeconds(-1));
            // Exactly at asOf - must be INCLUDED.
            await _fixture.InsertLogEntryAsync("Error", "boundary-inclusive-end", asOf);
            // One second after asOf (a "future" error relative to asOf) - must be EXCLUDED.
            await _fixture.InsertLogEntryAsync("Error", "boundary-exclusive-end", asOf.AddSeconds(1));
            // Comfortably inside the window - INCLUDED.
            await _fixture.InsertLogEntryAsync("Error", "well-inside-window", asOf.AddHours(-1));
            // A Warning inside the window - must NOT count toward ErrorCount24h at all,
            // regardless of recency (only Level == "Error" counts).
            await _fixture.InsertLogEntryAsync("Warning", "warning-not-an-error", asOf.AddHours(-1), eventCategory: "Security");

            var summary = await dashboardService.GetSummaryAsync(asOf);

            Assert.Equal(3, summary.ErrorCount24h);
        }

        [Fact]
        public async Task GetSummaryAsync_RecentActivity_OrdersDescendingAndExcludesEntriesAfterAsOf()
        {
            using var scope = _fixture.CreateScope();
            var dashboardService = scope.ServiceProvider.GetRequiredService<IAdminDashboardService>();

            var asOf = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
            var oldest = asOf.AddMinutes(-30);
            var middle = asOf.AddMinutes(-10);
            var newest = asOf.AddMinutes(-1);
            var future = asOf.AddMinutes(5); // after asOf - must never appear

            await _fixture.InsertLogEntryAsync("Warning", "recent-activity-oldest", oldest, eventCategory: "Security");
            await _fixture.InsertLogEntryAsync("Warning", "recent-activity-middle", middle, eventCategory: "Security");
            await _fixture.InsertLogEntryAsync("Warning", "recent-activity-newest", newest, eventCategory: "Security");
            await _fixture.InsertLogEntryAsync("Warning", "recent-activity-future", future, eventCategory: "Security");

            var summary = await dashboardService.GetSummaryAsync(asOf, recentActivityCount: 3);

            Assert.DoesNotContain(summary.RecentActivity, e => e.Message == "recent-activity-future");
            var ourRows = summary.RecentActivity
                .Where(e => e.Message != null && e.Message.StartsWith("recent-activity-"))
                .ToList();
            Assert.Equal(
                new[] { "recent-activity-newest", "recent-activity-middle", "recent-activity-oldest" },
                ourRows.Select(e => e.Message));
        }
    }
}
