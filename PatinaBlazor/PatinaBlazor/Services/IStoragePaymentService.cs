using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class ReserveUnitResult
    {
        public bool Succeeded { get; init; }
        public StorageRental? Rental { get; init; }
        public string? Error { get; init; }
    }

    public class StartVaultSetupResult
    {
        public bool Succeeded { get; init; }
        public string? ApproveUrl { get; init; }
        public string? Error { get; init; }
    }

    public class StartCardSetupResult
    {
        public bool Succeeded { get; init; }
        public string? SetupTokenId { get; init; }
        public string? ClientToken { get; init; }
        public string? SdkScriptUrl { get; init; }
        public string? Error { get; init; }
    }

    public class ChargeRentalResult
    {
        public bool Succeeded { get; init; }
        public decimal Amount { get; init; }
        public string? Error { get; init; }
    }

    public class CompleteVaultSetupResult
    {
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
        public ChargeRentalResult? Charge { get; init; }
    }

    public class FinalizeVaultSetupResult
    {
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
        public string? DisplayLabel { get; init; }
    }

    public interface IStoragePaymentService
    {
        // Atomically claims an Available unit for a customer, self-service (unlike
        // StorageService.StartRentalAsync, which is admin-driven) - creates a
        // PendingPayment rental with a default billing frequency, real activation
        // (Active + Occupied) waits for a completed payment step. See Phase 2 of the
        // 2026-09 Phase 2b checkpoint entry for why billing frequency/payment aren't
        // decided here yet.
        Task<ReserveUnitResult> ReserveUnitAsync(int unitId, string customerUserId);

        // Records the customer's chosen billing frequency on the rental and starts a
        // PayPal Vault setup - returns the URL to redirect the customer to for approval.
        Task<StartVaultSetupResult> StartVaultSetupAsync(int rentalId, BillingFrequency billingFrequency, string returnUrl, string cancelUrl);

        // The card-entry equivalent of StartVaultSetupAsync above - records billing
        // frequency, then returns everything the browser's Card Fields JS SDK needs to
        // initialize and collect a card directly. Usually completes inline with no
        // redirect (this app's own Blazor Server circuit stays alive the whole time), but
        // PayPal's API still requires returnUrl/cancelUrl on the underlying setup token for
        // the rare case a 3DS/SCA challenge does redirect the buyer - reuses the same
        // return page the caller already built for the PayPal Wallet flow.
        Task<StartCardSetupResult> StartCardSetupAsync(int rentalId, BillingFrequency billingFrequency, string returnUrl, string cancelUrl);

        // Finalizes an approved setup token into a reusable payment token and
        // saves/replaces the customer's StoragePaymentMethod - no charge. Used directly by
        // the wallet page's "Replace Payment Method" flow (charging again there would
        // double-bill an already-current cycle); CompleteVaultSetupAsync below calls this
        // too, as the first half of the real signup flow.
        Task<FinalizeVaultSetupResult> FinalizeVaultSetupAsync(int rentalId, string setupTokenId);

        // Called from the signup wizard's return page once the customer has approved:
        // FinalizeVaultSetupAsync, then charges the first cycle via ChargeRentalAsync. The
        // payment method is saved even if the subsequent charge fails, so a failed first
        // charge doesn't force the customer to redo PayPal approval.
        Task<CompleteVaultSetupResult> CompleteVaultSetupAsync(int rentalId, string setupTokenId);

        // Charges the rental's saved payment method for its next due cycle (GetNextDueDate) -
        // used both for the first charge at signup and, in Part 3, by the nightly billing
        // job. Records a StoragePaymentTransaction row regardless of outcome. On success,
        // PendingPayment rentals become Active (and their unit Occupied); an already-Active
        // rental whose charge fails becomes PaymentIssue.
        Task<ChargeRentalResult> ChargeRentalAsync(int rentalId);

        // Wallet-page reads.
        Task<StoragePaymentMethod?> GetPaymentMethodForCustomerAsync(string customerUserId);
        Task<List<StoragePaymentTransaction>> GetTransactionsForRentalAsync(int rentalId);
    }
}
