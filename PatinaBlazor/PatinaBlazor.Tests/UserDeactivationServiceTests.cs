using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers the real fix for the "deleting a user throws an exception" bug: per explicit
    // user direction ("Maybe when we 'delete' users, we should just inactivate them... I just
    // want to keep their payments records and rental agreements intact"), the admin "delete"
    // action no longer deletes anything at all - it locks the account out via ASP.NET Core
    // Identity's real lockout mechanism instead, leaving every StorageRental,
    // StoragePaymentTransaction, Collectable, Article, etc. completely untouched regardless
    // of what the target user is linked to. This supersedes the earlier UserDeletionService,
    // which blocked deletion with a friendly error instead - no longer needed, since nothing
    // is deleted any more.
    [Collection("Database")]
    public class UserDeactivationServiceTests
    {
        private readonly DatabaseFixture _fixture;

        public UserDeactivationServiceTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task SetActiveAsync_Deactivate_UserGenuinelyCannotSignInAnymore()
        {
            using var scope = _fixture.CreateScope();
            var deactivationService = scope.ServiceProvider.GetRequiredService<IUserDeactivationService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await CreateThrowawayUserAsync(userManager, "deactivationtest_signin");
            Assert.False(await userManager.IsLockedOutAsync(user));

            var result = await deactivationService.SetActiveAsync(user, active: false);

            Assert.True(result.Succeeded, result.Error);
            var reloaded = await userManager.FindByIdAsync(user.Id);
            Assert.True(await userManager.IsLockedOutAsync(reloaded!));
        }

        [Fact]
        public async Task SetActiveAsync_Reactivate_ClearsTheLockoutAndSignInWorksAgain()
        {
            using var scope = _fixture.CreateScope();
            var deactivationService = scope.ServiceProvider.GetRequiredService<IUserDeactivationService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await CreateThrowawayUserAsync(userManager, "deactivationtest_reactivate");
            await deactivationService.SetActiveAsync(user, active: false);
            Assert.True(await userManager.IsLockedOutAsync(await userManager.FindByIdAsync(user.Id) ?? user));

            var result = await deactivationService.SetActiveAsync(user, active: true);

            Assert.True(result.Succeeded, result.Error);
            var reloaded = await userManager.FindByIdAsync(user.Id);
            Assert.False(await userManager.IsLockedOutAsync(reloaded!));
        }

        [Fact]
        public async Task SetActiveAsync_UserIsCustomerOnARentalWithPaymentHistory_DeactivatesSuccessfullyAndPreservesEverything()
        {
            // This is the exact case that used to throw a raw exception (StorageRental.
            // CustomerUserId is a Restrict FK) - deactivating instead of deleting sidesteps
            // that entirely, and must leave the rental/payment rows completely untouched.
            using var scope = _fixture.CreateScope();
            var deactivationService = scope.ServiceProvider.GetRequiredService<IUserDeactivationService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var customer = await CreateThrowawayUserAsync(userManager, "deactivationtest_customer");

            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = $"Deactivation Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Test Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);
            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "DEACT-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = 100m
            }, admin.Id);
            var startDate = new DateTime(2026, 1, 1);
            var rental = await storageService.StartRentalAsync(unit.Id, customer.Id, unit.MonthlyRate, startDate, BillingFrequency.Monthly, startDate, admin.Id);

            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                context.StoragePaymentTransactions.Add(new StoragePaymentTransaction
                {
                    StorageRentalId = rental.Id,
                    PayPalOrderId = "ORDER-DEACTIVATION-TEST",
                    Amount = 100m,
                    Succeeded = true,
                    OccurredAtUtc = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            var result = await deactivationService.SetActiveAsync(customer, active: false);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(await userManager.FindByIdAsync(customer.Id));

            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);
                Assert.NotNull(reloadedRental);
                Assert.Equal(customer.Id, reloadedRental!.CustomerUserId);

                var transaction = await context.StoragePaymentTransactions.SingleAsync(t => t.StorageRentalId == rental.Id);
                Assert.True(transaction.Succeeded);
                Assert.Equal(100m, transaction.Amount);
            }
        }

        [Fact]
        public async Task SetActiveAsync_UserHasACollectable_DeactivatesSuccessfullyAndTheCollectableIsUntouched()
        {
            using var scope = _fixture.CreateScope();
            var deactivationService = scope.ServiceProvider.GetRequiredService<IUserDeactivationService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await CreateThrowawayUserAsync(userManager, "deactivationtest_collectable");

            Guid collectableId;
            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var collectable = new Collectable
                {
                    UserId = user.Id,
                    Description = "Deactivation Test Collectable - should survive deactivation."
                };
                context.Collectables.Add(collectable);
                await context.SaveChangesAsync();
                collectableId = collectable.Id;
            }

            var result = await deactivationService.SetActiveAsync(user, active: false);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(await userManager.FindByIdAsync(user.Id));

            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                Assert.NotNull(await context.Collectables.FindAsync(collectableId));
            }
        }

        private static async Task<ApplicationUser> CreateThrowawayUserAsync(UserManager<ApplicationUser> userManager, string prefix)
        {
            var email = $"{prefix}_{Guid.NewGuid():N}@patinablazor.local";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "ThrowawayTest123!");
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
            return user;
        }
    }
}
