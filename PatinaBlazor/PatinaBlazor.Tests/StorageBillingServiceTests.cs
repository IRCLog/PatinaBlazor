using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using PatinaBlazor.Tests.Fakes;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers StorageBillingService.RunDueBillingAsync - the selection-and-charging logic
    // behind the nightly StorageBillingHostedService (Part 3 of the 2026-09 Phase 2b
    // checkpoint entry), extracted specifically so it's directly callable here instead of
    // needing to wait on a real BackgroundService timer loop. Tested against
    // FakePayPalClient, same as StoragePaymentServiceVaultTests - no real PayPal calls.
    [Collection("Database")]
    public class StorageBillingServiceTests
    {
        private readonly DatabaseFixture _fixture;

        public StorageBillingServiceTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RunDueBillingAsync_RentalDueToday_ChargesItAndActivatesRemainsActive()
        {
            using var scope = _fixture.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IStorageBillingService>();

            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            var asOf = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var (rental, _) = await CreateActiveRentalAsync(scope, rate: 100m, paymentDate: asOf.AddDays(-5), lastBilledDate: null);

            // BillingRunResult's counts cover every billable rental in the shared fixture's
            // DB, including seeded dummy data whose PaymentDate can legitimately fall due
            // for a fixed-in-the-past asOf like this one - not just the one rental this test
            // created, so they're not asserted on directly (see the note on the class). This
            // scope's own FakePayPalClient instance, however, is never shared with any other
            // test - exactly one charge call for exactly this rental's vault id is a safe,
            // meaningful assertion.
            await billingService.RunDueBillingAsync(asOf);

            var chargeCall = Assert.Single(fakePayPal.ChargeCalls);
            Assert.Equal(100m, chargeCall.Amount);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var reloaded = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.Active, reloaded!.Status);
            Assert.Equal(rental.PaymentDate.Date, reloaded.LastBilledDate!.Value.Date);
        }

        [Fact]
        public async Task RunDueBillingAsync_RentalNotYetDue_SkipsItAndNeverCallsPayPal()
        {
            using var scope = _fixture.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IStorageBillingService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();

            var asOf = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var (rental, _) = await CreateActiveRentalAsync(scope, rate: 100m, paymentDate: asOf.AddDays(5), lastBilledDate: null);

            await billingService.RunDueBillingAsync(asOf);

            // This scope's FakePayPalClient is never shared with another test, and no other
            // rental this test created could be in it - a clean, safe assertion regardless
            // of what the shared fixture's seeded data or other tests' already-billed
            // rentals look like.
            Assert.Empty(fakePayPal.ChargeCalls);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            Assert.Null((await context.StorageRentals.FindAsync(rental.Id))!.LastBilledDate);
        }

        [Fact]
        public async Task RunDueBillingAsync_ChargeFails_LastBilledDateStaysUnchangedSoItIsRetriedTheNextRun()
        {
            using var scope = _fixture.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IStorageBillingService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.ChargeSucceeds = false;
            fakePayPal.ChargeError = "INSTRUMENT_DECLINED";

            var asOf = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var (rental, _) = await CreateActiveRentalAsync(scope, rate: 100m, paymentDate: asOf.AddDays(-5), lastBilledDate: null);

            await billingService.RunDueBillingAsync(asOf);
            Assert.Single(fakePayPal.ChargeCalls);

            await using (var context = await _fixture.DbContextFactory.CreateDbContextAsync())
            {
                var reloaded = await context.StorageRentals.FindAsync(rental.Id);
                Assert.Null(reloaded!.LastBilledDate);
                Assert.Equal(StorageRentalStatus.PaymentIssue, reloaded.Status);
            }

            // A later run (the "next night") must pick the still-unpaid rental back up -
            // this is the entire retry mechanism, no separate scheduling needed. Same
            // FakePayPalClient/scope throughout this test, so a second recorded call can
            // only be this same rental being retried.
            await billingService.RunDueBillingAsync(asOf.AddDays(1));
            Assert.Equal(2, fakePayPal.ChargeCalls.Count);
        }

        [Fact]
        public async Task RunDueBillingAsync_MultipleDueRentalsAndOneNotDue_OnlyChargesTheDueOnes()
        {
            using var scope = _fixture.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IStorageBillingService>();

            var asOf = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var (dueA, _) = await CreateActiveRentalAsync(scope, rate: 100m, paymentDate: asOf.AddDays(-1), lastBilledDate: null);
            var (dueB, _) = await CreateActiveRentalAsync(scope, rate: 200m, paymentDate: asOf, lastBilledDate: null);
            var (notDue, _) = await CreateActiveRentalAsync(scope, rate: 300m, paymentDate: asOf.AddDays(10), lastBilledDate: null);

            await billingService.RunDueBillingAsync(asOf);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            Assert.NotNull((await context.StorageRentals.FindAsync(dueA.Id))!.LastBilledDate);
            Assert.NotNull((await context.StorageRentals.FindAsync(dueB.Id))!.LastBilledDate);
            Assert.Null((await context.StorageRentals.FindAsync(notDue.Id))!.LastBilledDate);
        }

        // Creates a rental directly as Active (bypassing the normal reserve/vault-setup
        // flow, which would anchor PaymentDate to "now") so tests can control exactly which
        // cycle is or isn't due relative to a fixed asOf - and gives it a real saved
        // StoragePaymentMethod, since ChargeRentalAsync requires one on file.
        private async Task<(StorageRental Rental, StorageUnit Unit)> CreateActiveRentalAsync(
            IServiceScope scope, decimal rate, DateTime paymentDate, DateTime? lastBilledDate, BillingFrequency frequency = BillingFrequency.Monthly)
        {
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var customerId = await CreateThrowawayCustomerAsync(userManager);

            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = $"Billing Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Billing Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);

            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "B-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = rate
            }, admin.Id);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();

            var now = DateTime.UtcNow;
            var rental = new StorageRental
            {
                StorageUnitId = unit.Id,
                CustomerUserId = customerId,
                StartDate = paymentDate,
                PaymentDate = paymentDate,
                LastBilledDate = lastBilledDate,
                MonthlyRateAtSigning = rate,
                BillingFrequency = frequency,
                Status = StorageRentalStatus.Active,
                CreatedDate = now,
                ModifiedDate = now,
                CreatedByUserId = customerId,
                ModifiedByUserId = customerId
            };
            context.StorageRentals.Add(rental);

            context.StoragePaymentMethods.Add(new StoragePaymentMethod
            {
                UserId = customerId,
                PayPalPaymentTokenId = $"VAULT-{Guid.NewGuid():N}",
                DisplayLabel = "test@example.com",
                CreatedDate = now
            });

            await context.SaveChangesAsync();

            return (rental, unit);
        }

        private static async Task<string> CreateThrowawayCustomerAsync(UserManager<ApplicationUser> userManager)
        {
            var email = $"billingtest_{Guid.NewGuid():N}@patinablazor.local";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "ThrowawayTest123!");
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
            return user.Id;
        }
    }
}
