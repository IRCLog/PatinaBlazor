using System.ComponentModel.DataAnnotations;

namespace PatinaBlazor.Data
{
    // A saved PayPal Vault payment method (PayPal's Payment Method Tokens API, not a
    // Subscription - see the 2026-09 Phase 2b checkpoint entry for why). One row per
    // customer, shared-primary-key with ApplicationUser - saving a new one replaces the
    // customer's current one, matching the wallet page's actual need (a single payment
    // method on file, not a multi-card list).
    public class StoragePaymentMethod
    {
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        [Required]
        [StringLength(50)]
        public string PayPalPaymentTokenId { get; set; } = string.Empty;

        // Which payment_source shape this vault_id belongs to - PayPalClient.
        // ChargeVaultedPaymentMethodAsync needs this to build the right charge request
        // (payment_source.paypal.vault_id vs payment_source.card.vault_id).
        [Required]
        public PaymentSourceType SourceType { get; set; } = PaymentSourceType.PayPal;

        // A human-readable label for the wallet page - the payer's PayPal email for a
        // PayPal-sourced method, or "{brand} ending in {last 4}" for a card, both taken
        // directly from the Vault API's response at save time.
        [StringLength(200)]
        public string? DisplayLabel { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}
