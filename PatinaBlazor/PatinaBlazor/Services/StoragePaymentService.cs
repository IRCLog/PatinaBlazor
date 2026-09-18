using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatinaBlazor.Data;
using PatinaBlazor.Services.PayPal;

namespace PatinaBlazor.Services
{
    // Uses IDbContextFactory rather than an injected scoped ApplicationDbContext - see the
    // same note on ArticleService/StorageService/etc. Every method here gets its own
    // short-lived context.
    public class StoragePaymentService : IStoragePaymentService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IPayPalClient _payPalClient;
        private readonly string _cardFieldsSdkScriptUrl;

        public StoragePaymentService(IDbContextFactory<ApplicationDbContext> contextFactory, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions)
        {
            _contextFactory = contextFactory;
            _payPalClient = payPalClient;

            // The Card Fields JS SDK is loaded from a different host for sandbox vs live -
            // derived from the same BaseUrl that already governs which REST host every
            // server-side PayPal call goes to, so the two can never drift out of sync.
            _cardFieldsSdkScriptUrl = payPalOptions.Value.BaseUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://www.sandbox.paypal.com/web-sdk/v6/core"
                : "https://www.paypal.com/web-sdk/v6/core";
        }

        public async Task<ReserveUnitResult> ReserveUnitAsync(int unitId, string customerUserId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var now = DateTime.UtcNow;

            // A customer revisiting the wizard and picking a different unit than before is
            // a real, plausible flow (not just a hypothetical edge case) - release any
            // earlier reservation of theirs first, rather than leaving it stuck Reserved
            // forever or letting them accumulate more than one pending rental.
            var previousPending = await context.StorageRentals
                .Include(r => r.Unit)
                .Where(r => r.CustomerUserId == customerUserId && r.Status == StorageRentalStatus.PendingPayment)
                .ToListAsync();
            foreach (var previous in previousPending)
            {
                previous.Status = StorageRentalStatus.Ended;
                previous.EndDate = now;
                previous.ModifiedDate = now;
                if (previous.Unit != null)
                {
                    previous.Unit.Status = StorageUnitStatus.Available;
                    previous.Unit.ModifiedDate = now;
                }
            }
            if (previousPending.Count > 0)
            {
                await context.SaveChangesAsync();
            }

            // Atomic conditional update, not a read-then-write - two customers could
            // otherwise both read "Available" before either commits, and both claim the
            // same physical unit. A plain UPDATE ... WHERE Status = 'Available' either
            // affects exactly one row (this request won) or zero (someone else's request
            // already claimed it, or it's genuinely unavailable) - checked via the
            // returned row count, not re-read afterward.
            var rowsAffected = await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [StorageUnits] SET [Status] = 'Reserved', [ModifiedDate] = {now}
                WHERE [Id] = {unitId} AND [Status] = 'Available'
                """);

            if (rowsAffected == 0)
            {
                return new ReserveUnitResult { Succeeded = false, Error = "That unit is no longer available. Please pick another." };
            }

            var unit = await context.StorageUnits.FirstAsync(u => u.Id == unitId);

            var rental = new StorageRental
            {
                StorageUnitId = unitId,
                CustomerUserId = customerUserId,
                StartDate = now,
                PaymentDate = now,
                MonthlyRateAtSigning = unit.MonthlyRate,
                BillingFrequency = BillingFrequency.Monthly,
                Status = StorageRentalStatus.PendingPayment,
                CreatedDate = now,
                ModifiedDate = now,
                CreatedByUserId = customerUserId,
                ModifiedByUserId = customerUserId
            };

            context.StorageRentals.Add(rental);
            await context.SaveChangesAsync();

            return new ReserveUnitResult { Succeeded = true, Rental = rental };
        }

        public async Task<StartVaultSetupResult> StartVaultSetupAsync(int rentalId, BillingFrequency billingFrequency, string returnUrl, string cancelUrl)
        {
            var updated = await UpdateBillingFrequencyAsync(rentalId, billingFrequency);
            if (!updated)
            {
                return new StartVaultSetupResult { Succeeded = false, Error = "Rental not found." };
            }

            var setupToken = await _payPalClient.CreateSetupTokenAsync(returnUrl, cancelUrl);
            if (!setupToken.Succeeded)
            {
                return new StartVaultSetupResult { Succeeded = false, Error = setupToken.Error };
            }

            return new StartVaultSetupResult { Succeeded = true, ApproveUrl = setupToken.ApproveUrl };
        }

        public async Task<StartCardSetupResult> StartCardSetupAsync(int rentalId, BillingFrequency billingFrequency, string returnUrl, string cancelUrl)
        {
            var updated = await UpdateBillingFrequencyAsync(rentalId, billingFrequency);
            if (!updated)
            {
                return new StartCardSetupResult { Succeeded = false, Error = "Rental not found." };
            }

            // The Card Fields JS SDK needs its own browser-safe client token (distinct from
            // the server's OAuth2 access token - see IPayPalClient's doc comment) alongside
            // a setup token to fill in, in the browser, via CardFields.submit().
            var clientToken = await _payPalClient.GetBrowserSafeClientTokenAsync();
            if (!clientToken.Succeeded)
            {
                return new StartCardSetupResult { Succeeded = false, Error = clientToken.Error };
            }

            var setupToken = await _payPalClient.CreateCardSetupTokenAsync(returnUrl, cancelUrl);
            if (!setupToken.Succeeded)
            {
                return new StartCardSetupResult { Succeeded = false, Error = setupToken.Error };
            }

            return new StartCardSetupResult
            {
                Succeeded = true,
                SetupTokenId = setupToken.SetupTokenId,
                ClientToken = clientToken.ClientToken,
                SdkScriptUrl = _cardFieldsSdkScriptUrl
            };
        }

        private async Task<bool> UpdateBillingFrequencyAsync(int rentalId, BillingFrequency billingFrequency)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var rental = await context.StorageRentals.FindAsync(rentalId);
            if (rental == null)
            {
                return false;
            }

            rental.BillingFrequency = billingFrequency;
            rental.ModifiedDate = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<FinalizeVaultSetupResult> FinalizeVaultSetupAsync(int rentalId, string setupTokenId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var rental = await context.StorageRentals.FindAsync(rentalId);
            if (rental == null)
            {
                return new FinalizeVaultSetupResult { Succeeded = false, Error = "Rental not found." };
            }

            var paymentToken = await _payPalClient.CreatePaymentTokenFromSetupTokenAsync(setupTokenId);
            if (!paymentToken.Succeeded)
            {
                return new FinalizeVaultSetupResult { Succeeded = false, Error = paymentToken.Error };
            }

            var now = DateTime.UtcNow;
            var existingMethod = await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId);
            if (existingMethod != null)
            {
                existingMethod.PayPalPaymentTokenId = paymentToken.PaymentTokenId!;
                existingMethod.SourceType = paymentToken.SourceType;
                existingMethod.DisplayLabel = paymentToken.DisplayLabel;
                existingMethod.CreatedDate = now;
                context.StoragePaymentMethods.Update(existingMethod);
            }
            else
            {
                context.StoragePaymentMethods.Add(new StoragePaymentMethod
                {
                    UserId = rental.CustomerUserId,
                    PayPalPaymentTokenId = paymentToken.PaymentTokenId!,
                    SourceType = paymentToken.SourceType,
                    DisplayLabel = paymentToken.DisplayLabel,
                    CreatedDate = now
                });
            }
            await context.SaveChangesAsync();

            return new FinalizeVaultSetupResult { Succeeded = true, DisplayLabel = paymentToken.DisplayLabel };
        }

        public async Task<CompleteVaultSetupResult> CompleteVaultSetupAsync(int rentalId, string setupTokenId)
        {
            var finalizeResult = await FinalizeVaultSetupAsync(rentalId, setupTokenId);
            if (!finalizeResult.Succeeded)
            {
                return new CompleteVaultSetupResult { Succeeded = false, Error = finalizeResult.Error };
            }

            var chargeResult = await ChargeRentalAsync(rentalId);
            return new CompleteVaultSetupResult { Succeeded = true, Charge = chargeResult };
        }

        public async Task<ChargeRentalResult> ChargeRentalAsync(int rentalId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var rental = await context.StorageRentals.Include(r => r.Unit).FirstAsync(r => r.Id == rentalId);
            var paymentMethod = await context.StoragePaymentMethods.FindAsync(rental.CustomerUserId);
            if (paymentMethod == null)
            {
                return new ChargeRentalResult { Succeeded = false, Error = "No payment method on file." };
            }

            // The cycle actually being paid for - computed before LastBilledDate is
            // advanced, since GetNextDueDate()'s result depends on the current value.
            var dueDate = rental.GetNextDueDate();
            var amount = rental.GetChargeAmount();

            var chargeResult = await _payPalClient.ChargeVaultedPaymentMethodAsync(paymentMethod.PayPalPaymentTokenId, paymentMethod.SourceType, amount);

            var now = DateTime.UtcNow;
            context.StoragePaymentTransactions.Add(new StoragePaymentTransaction
            {
                StorageRentalId = rental.Id,
                PayPalOrderId = chargeResult.OrderId,
                Amount = amount,
                Succeeded = chargeResult.Succeeded,
                FailureReason = chargeResult.Succeeded ? null : chargeResult.Error,
                OccurredAtUtc = now
            });

            if (chargeResult.Succeeded)
            {
                rental.LastBilledDate = dueDate;
                rental.Status = StorageRentalStatus.Active;
                rental.ModifiedDate = now;
                if (rental.Unit != null)
                {
                    rental.Unit.Status = StorageUnitStatus.Occupied;
                    rental.Unit.ModifiedDate = now;
                }
            }
            else if (rental.Status == StorageRentalStatus.Active)
            {
                rental.Status = StorageRentalStatus.PaymentIssue;
                rental.ModifiedDate = now;
            }

            await context.SaveChangesAsync();

            return new ChargeRentalResult { Succeeded = chargeResult.Succeeded, Amount = amount, Error = chargeResult.Error };
        }

        public async Task<StoragePaymentMethod?> GetPaymentMethodForCustomerAsync(string customerUserId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.StoragePaymentMethods.FindAsync(customerUserId);
        }

        public async Task<List<StoragePaymentTransaction>> GetTransactionsForRentalAsync(int rentalId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.StoragePaymentTransactions
                .Where(t => t.StorageRentalId == rentalId)
                .OrderByDescending(t => t.OccurredAtUtc)
                .ToListAsync();
        }
    }
}
