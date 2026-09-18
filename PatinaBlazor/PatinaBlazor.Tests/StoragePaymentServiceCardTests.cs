using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using PatinaBlazor.Tests.Fakes;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers StoragePaymentService's card-vaulting path (StartCardSetupAsync, and
    // FinalizeVaultSetupAsync/CompleteVaultSetupAsync/ChargeRentalAsync when the underlying
    // PayPal payment token is card-sourced rather than PayPal-Wallet-sourced) - the
    // "Card entry as a second payment option" addendum to the 2026-09 Phase 2b checkpoint
    // entry. Tested against FakePayPalClient, same as StoragePaymentServiceVaultTests.
    [Collection("Database")]
    public class StoragePaymentServiceCardTests
    {
        private readonly DatabaseFixture _fixture;

        public StoragePaymentServiceCardTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StartCardSetupAsync_Succeeds_UpdatesBillingFrequencyAndReturnsTokensForTheBrowser()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.ClientToken = "REAL-LOOKING-CLIENT-TOKEN";
            fakePayPal.SetupTokenId = "CARD-SETUP-TOKEN-1";

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 120m);

            var result = await paymentService.StartCardSetupAsync(rental.Id, BillingFrequency.Annually, "https://example.com/return", "https://example.com/cancel");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("REAL-LOOKING-CLIENT-TOKEN", result.ClientToken);
            Assert.Equal("CARD-SETUP-TOKEN-1", result.SetupTokenId);
            Assert.False(string.IsNullOrEmpty(result.SdkScriptUrl));

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var reloaded = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(BillingFrequency.Annually, reloaded!.BillingFrequency);
        }

        [Fact]
        public async Task StartCardSetupAsync_ClientTokenFetchFails_ReturnsErrorAndNeverCreatesASetupToken()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.ClientTokenSucceeds = false;
            fakePayPal.ClientTokenError = "PayPal auth is down.";

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.StartCardSetupAsync(rental.Id, BillingFrequency.Monthly, "https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("PayPal auth is down.", result.Error);
            Assert.Null(result.SetupTokenId);
        }

        [Fact]
        public async Task CompleteVaultSetupAsync_CardSourced_PersistsCardSourceTypeAndChargesWithCardShape()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.PaymentTokenId = "CARD-VAULT-ID-1";
            fakePayPal.PaymentTokenSourceType = PaymentSourceType.Card;
            fakePayPal.DisplayLabel = "Visa ending in 4242";

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 90m);

            var result = await paymentService.CompleteVaultSetupAsync(rental.Id, "CARD-SETUP-TOKEN-2");

            Assert.True(result.Succeeded, result.Error);
            Assert.True(result.Charge!.Succeeded, result.Charge.Error);

            var chargeCall = Assert.Single(fakePayPal.ChargeCalls);
            Assert.Equal("CARD-VAULT-ID-1", chargeCall.VaultId);
            Assert.Equal(PaymentSourceType.Card, chargeCall.SourceType);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var paymentMethod = await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId);
            Assert.NotNull(paymentMethod);
            Assert.Equal(PaymentSourceType.Card, paymentMethod!.SourceType);
            Assert.Equal("Visa ending in 4242", paymentMethod.DisplayLabel);
        }

        [Fact]
        public async Task FinalizeVaultSetupAsync_CardSourced_SavesPaymentMethodButNeverCharges()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.PaymentTokenId = "CARD-VAULT-ID-2";
            fakePayPal.PaymentTokenSourceType = PaymentSourceType.Card;
            fakePayPal.DisplayLabel = "Mastercard ending in 5555";

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);

            var result = await paymentService.FinalizeVaultSetupAsync(rental.Id, "CARD-SETUP-TOKEN-3");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("Mastercard ending in 5555", result.DisplayLabel);

            // FinalizeVaultSetupAsync is the wallet "Replace Payment Method" path - it must
            // never call PayPal's charge endpoint, regardless of source type, since that
            // would double-bill an already-current cycle. Real regression risk: it's easy to
            // accidentally wire this to CompleteVaultSetupAsync instead.
            Assert.Empty(fakePayPal.ChargeCalls);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var paymentMethod = await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId);
            Assert.NotNull(paymentMethod);
            Assert.Equal(PaymentSourceType.Card, paymentMethod!.SourceType);

            var reloadedRental = await context.StorageRentals.FindAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.PendingPayment, reloadedRental!.Status);
        }

        [Fact]
        public async Task ChargeRentalAsync_ExistingCardPaymentMethod_ChargesWithCardShapeNotPayPalShape()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var fakePayPal = scope.ServiceProvider.GetRequiredService<FakePayPalClient>();
            fakePayPal.PaymentTokenSourceType = PaymentSourceType.Card;

            var (rental, _) = await CreatePendingRentalAsync(scope, rate: 100m);
            var first = await paymentService.CompleteVaultSetupAsync(rental.Id, "CARD-SETUP-TOKEN-4");
            Assert.True(first.Charge!.Succeeded);

            var result = await paymentService.ChargeRentalAsync(rental.Id);

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(2, fakePayPal.ChargeCalls.Count);
            Assert.All(fakePayPal.ChargeCalls, call => Assert.Equal(PaymentSourceType.Card, call.SourceType));
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
                Name = $"Card Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Card Test Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);

            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "C-01",
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
            var email = $"cardtest_{Guid.NewGuid():N}@patinablazor.local";
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
