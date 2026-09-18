using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using PatinaBlazor.Tests.Fakes;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers StoragePaymentService's PayPal Vault orchestration (StartVaultSetupAsync,
    // CompleteVaultSetupAsync, ChargeRentalAsync) - Part 2 of the 2026-09 Phase 2b
    // checkpoint entry. Tested against FakePayPalClient, never the real PayPal sandbox -
    // matches the plan's explicit testing strategy (CI has no PayPal secrets); real sandbox
    // verification is a separate, manual pass.
    [Collection("Database")]
    public class StoragePaymentServiceVaultTests
    {
        private readonly DatabaseFixture _fixture;

        public StoragePaymentServiceVaultTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StartVaultSetupAsync_Succeeds_UpdatesBillingFrequencyAndReturnsApproveUrl()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.ApproveUrl = "https://sandbox.paypal.com/agreements/approve?approval_session_id=ABC123";

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.StartVaultSetupAsync(rental.Id, BillingFrequency.Quarterly, "https://example.com/return", "https://example.com/cancel");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("https://sandbox.paypal.com/agreements/approve?approval_session_id=ABC123", result.ApproveUrl);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var reloaded = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(BillingFrequency.Quarterly, reloaded!.BillingFrequency);
        }

        [Fact]
        public async Task StartVaultSetupAsync_PayPalSetupTokenFails_ReturnsErrorWithoutThrowing()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.SetupTokenSucceeds = false;
            fakePayPal.SetupTokenError = "PayPal is down.";

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.StartVaultSetupAsync(rental.Id, BillingFrequency.Monthly, "https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("PayPal is down.", result.Error);
        }

        [Fact]
        public async Task CompleteVaultSetupAsync_HappyPath_SavesPaymentMethodChargesFirstCycleAndActivatesRental()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.PaymentTokenId = "VAULT-ID-1";
            fakePayPal.DisplayLabel = "customer@example.com";

            var (rental, unit) = await CreatePendingRentalAsync(scope, rate: 150m, frequency: BillingFrequency.Monthly);

            var result = await paymentService.CompleteVaultSetupAsync(rental.Id, "SETUP-TOKEN-1");

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.Charge);
            Assert.True(result.Charge!.Succeeded, result.Charge.Error);
            Assert.Equal(150m, result.Charge.Amount);

            var chargeCall = Assert.Single(fakePayPal.ChargeCalls);
            Assert.Equal("VAULT-ID-1", chargeCall.VaultId);
            Assert.Equal(150m, chargeCall.Amount);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();

            var paymentMethod = await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId);
            Assert.NotNull(paymentMethod);
            Assert.Equal("VAULT-ID-1", paymentMethod!.PayPalPaymentTokenId);
            Assert.Equal("customer@example.com", paymentMethod.DisplayLabel);

            var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.Active, reloadedRental!.Status);
            Assert.NotNull(reloadedRental.LastBilledDate);

            var reloadedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Occupied, reloadedUnit!.Status);

            var transaction = await context.StoragePaymentTransactions.SingleAsync(t => t.StorageRentalId == rental.Id);
            Assert.True(transaction.Succeeded);
            Assert.Equal(150m, transaction.Amount);
            Assert.Equal("FAKE-ORDER-ID", transaction.PayPalOrderId);
        }

        [Fact]
        public async Task CompleteVaultSetupAsync_PaymentTokenFinalizeFails_DoesNotSavePaymentMethodOrTouchRental()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.PaymentTokenSucceeds = false;
            fakePayPal.PaymentTokenError = "The setup token was not approved.";

            var (rental, unit) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.CompleteVaultSetupAsync(rental.Id, "SETUP-TOKEN-BAD");

            Assert.False(result.Succeeded);
            Assert.Equal("The setup token was not approved.", result.Error);
            Assert.Null(result.Charge);
            Assert.Empty(fakePayPal.ChargeCalls);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            Assert.Null(await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId));

            var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.PendingPayment, reloadedRental!.Status);

            var reloadedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Reserved, reloadedUnit!.Status);
        }

        [Fact]
        public async Task CompleteVaultSetupAsync_ChargeDeclines_SavesPaymentMethodButRentalStaysPendingPayment()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.ChargeSucceeds = false;
            fakePayPal.ChargeError = "INSTRUMENT_DECLINED";

            var (rental, unit) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.CompleteVaultSetupAsync(rental.Id, "SETUP-TOKEN-2");

            // The overall operation "succeeded" in the sense that vaulting completed - the
            // caller inspects Charge.Succeeded separately to know the first payment itself failed.
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Charge);
            Assert.False(result.Charge!.Succeeded);
            Assert.Equal("INSTRUMENT_DECLINED", result.Charge.Error);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();

            // The payment method must still be saved - a declined first charge shouldn't
            // force the customer to redo PayPal approval to retry.
            Assert.NotNull(await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId));

            var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.PendingPayment, reloadedRental!.Status);
            Assert.Null(reloadedRental.LastBilledDate);

            var reloadedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Reserved, reloadedUnit!.Status);

            var transaction = await context.StoragePaymentTransactions.SingleAsync(t => t.StorageRentalId == rental.Id);
            Assert.False(transaction.Succeeded);
            Assert.Equal("INSTRUMENT_DECLINED", transaction.FailureReason);
        }

        [Fact]
        public async Task ChargeRentalAsync_NoPaymentMethodOnFile_FailsCleanlyWithoutCallingPayPal()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.ChargeRentalAsync(rental.Id);

            Assert.False(result.Succeeded);
            Assert.Equal("No payment method on file.", result.Error);
            Assert.Empty(fakePayPal.ChargeCalls);
        }

        [Fact]
        public async Task ChargeRentalAsync_ActiveRentalChargeFails_TransitionsToPaymentIssue()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);
            var firstCharge = await paymentService.CompleteVaultSetupAsync(rental.Id, "SETUP-TOKEN-3");
            Assert.True(firstCharge.Succeeded && firstCharge.Charge!.Succeeded);

            fakePayPal.ChargeSucceeds = false;
            fakePayPal.ChargeError = "INSUFFICIENT_FUNDS";

            var result = await paymentService.ChargeRentalAsync(rental.Id);

            Assert.False(result.Succeeded);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.PaymentIssue, reloadedRental!.Status);

            var transactions = await context.StoragePaymentTransactions.Where(t => t.StorageRentalId == rental.Id).OrderBy(t => t.OccurredAtUtc).ToListAsync();
            Assert.Equal(2, transactions.Count);
            Assert.True(transactions[0].Succeeded);
            Assert.False(transactions[1].Succeeded);
        }

        [Fact]
        public async Task ChargeRentalAsync_QuarterlyRental_ChargesRateTimesThreeAndAdvancesLastBilledDateToDueDateNotToday()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m, frequency: BillingFrequency.Quarterly);

            var result = await paymentService.CompleteVaultSetupAsync(rental.Id, "SETUP-TOKEN-4");

            Assert.True(result.Charge!.Succeeded);
            Assert.Equal(300m, result.Charge.Amount);

            var chargeCall = Assert.Single(fakePayPal.ChargeCalls);
            Assert.Equal(300m, chargeCall.Amount);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);

            // LastBilledDate must land on the rental's PaymentDate cycle (the due date being
            // paid), not on "today" - GetNextDueDate()'s whole point is separating the two.
            Assert.Equal(reloadedRental!.PaymentDate.Date, reloadedRental.LastBilledDate!.Value.Date);
        }

        // "Anchored to the due date, not to today" is indistinguishable from "anchored to
        // today" on a rental's very first charge (both land on the same moment for a
        // freshly-reserved rental). This test forces the two apart: two charges made back
        // to back must advance LastBilledDate to the *next* billing cycle, not repeat the
        // same date - which is exactly what would happen if ChargeRentalAsync used "today"
        // instead of GetNextDueDate().
        [Fact]
        public async Task ChargeRentalAsync_CalledAgainImmediatelyAfterTheFirstCharge_AdvancesToTheNextCycleNotTheSameDate()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m, frequency: BillingFrequency.Monthly);

            var first = await paymentService.CompleteVaultSetupAsync(rental.Id, "SETUP-TOKEN-5");
            Assert.True(first.Charge!.Succeeded);

            await using var afterFirst = await _fixture.DbContextFactory.CreateDbContextAsync();
            var firstLastBilledDate = (await afterFirst.StorageRentals.FindAsync(rental.Id))!.LastBilledDate!.Value;

            var second = await paymentService.ChargeRentalAsync(rental.Id);
            Assert.True(second.Succeeded, second.Error);

            await using var afterSecond = await _fixture.DbContextFactory.CreateDbContextAsync();
            var reloadedRental = await afterSecond.StorageRentals.FindAsync(rental.Id);

            Assert.Equal(firstLastBilledDate.AddMonths(1), reloadedRental!.LastBilledDate);
            Assert.NotEqual(firstLastBilledDate, reloadedRental.LastBilledDate);
        }

        private async Task<(StorageRental Rental, StorageUnit Unit)> CreatePendingRentalAsync(IServiceScope scope, decimal rate, BillingFrequency frequency = BillingFrequency.Monthly)
        {
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var customerId = await CreateThrowawayCustomerAsync(userManager);

            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = $"Vault Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Vault Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);

            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "V-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = rate
            }, admin.Id);

            var reserveResult = await paymentService.ReserveUnitAsync(unit.Id, customerId);
            Assert.True(reserveResult.Succeeded, reserveResult.Error);

            if (frequency != BillingFrequency.Monthly)
            {
                await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
                var rental = await context.StorageRentals.FindAsync(reserveResult.Rental!.Id);
                rental!.BillingFrequency = frequency;
                await context.SaveChangesAsync();
            }

            return (reserveResult.Rental!, unit);
        }

        private static async Task<string> CreateThrowawayCustomerAsync(UserManager<ApplicationUser> userManager)
        {
            var email = $"vaulttest_{Guid.NewGuid():N}@patinablazor.local";
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
