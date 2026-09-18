using PatinaBlazor.Data;

namespace PatinaBlazor.Services.PayPal
{
    public class PayPalClientTokenResult
    {
        public bool Succeeded { get; init; }
        public string? ClientToken { get; init; }
        public string? Error { get; init; }
    }

    public class PayPalSetupTokenResult
    {
        public bool Succeeded { get; init; }
        public string? SetupTokenId { get; init; }
        public string? ApproveUrl { get; init; }
        public string? Error { get; init; }
    }

    public class PayPalPaymentTokenResult
    {
        public bool Succeeded { get; init; }
        public string? PaymentTokenId { get; init; }
        public PaymentSourceType SourceType { get; init; }
        public string? DisplayLabel { get; init; }
        public string? Error { get; init; }
    }

    public class PayPalChargeResult
    {
        public bool Succeeded { get; init; }
        public string? OrderId { get; init; }
        public string? Error { get; init; }
    }

    // Wraps PayPal's Payment Method Tokens (Vault) v3 API and the Orders v2 API's
    // vault_id charge path - deliberately NOT the Subscriptions/Plans API, per the
    // 2026-09-16 checkpoint entry's explanation of why. Real request/response shapes
    // verified against PayPal's own OpenAPI specs and official SDK source before this was
    // written, not guessed - see that checkpoint entry.
    public interface IPayPalClient
    {
        // Step 1 of saving a PayPal Wallet payment method: creates a setup token and returns
        // the URL to redirect the customer to for approval. PayPal appends
        // "approval_token_id=<setup token id>" to returnUrl once the customer completes
        // approval - confirmed against PayPal's own official SDKs (paypal-ios, several
        // merchant plugins), since the OpenAPI spec itself doesn't document the redirect's
        // query parameter name.
        Task<PayPalSetupTokenResult> CreateSetupTokenAsync(string returnUrl, string cancelUrl, CancellationToken cancellationToken = default);

        // The card-vaulting equivalent of CreateSetupTokenAsync above - creates a setup
        // token with verification_method=SCA_WHEN_REQUIRED, per PayPal's own official Card
        // Fields server sample. Usually completes without any redirect (the browser's Card
        // Fields SDK fills in the card details against this same setup token id directly,
        // in place, via CardFieldsSavePaymentSession.submit(setupTokenId)), but returnUrl/
        // cancelUrl are still required by PayPal's API for the rare case a 3DS/SCA
        // challenge does need to redirect the buyer - reuses the same return page pattern
        // as the PayPal Wallet flow for that case, not a separate mechanism.
        Task<PayPalSetupTokenResult> CreateCardSetupTokenAsync(string returnUrl, string cancelUrl, CancellationToken cancellationToken = default);

        // A short-lived, browser-safe token (distinct from the server's own OAuth2 access
        // token - PayPal's docs are explicit these are not interchangeable) that the Card
        // Fields JS SDK needs client-side to initialize (window.paypal.createInstance
        // ({ clientToken, components: ["card-fields"] })). Generated via POST
        // /v1/oauth2/token with response_type=client_token - domains[] is deliberately never
        // sent, since PayPal rejects "localhost"/unregistered hosts with invalid_domain and
        // this app has no registered production domain configured for it yet.
        Task<PayPalClientTokenResult> GetBrowserSafeClientTokenAsync(CancellationToken cancellationToken = default);

        // Step 2 (both flows): exchanges an approved setup token for a reusable payment
        // token (the "vault_id" used to charge later with the customer not present). Same
        // call regardless of whether the underlying setup token is PayPal- or card-sourced -
        // only the response's payment_source branch differs, handled internally.
        Task<PayPalPaymentTokenResult> CreatePaymentTokenFromSetupTokenAsync(string setupTokenId, CancellationToken cancellationToken = default);

        // Charges a previously-vaulted payment method for a one-time amount via a
        // Create+Capture Order (intent=CAPTURE) - synchronous, the result comes back in
        // this same call, no webhook needed for the core flow. sourceType picks the request
        // shape: payment_source.paypal.vault_id vs payment_source.card.vault_id - the same
        // vault_id string works for either, PayPal validates it server-side regardless.
        Task<PayPalChargeResult> ChargeVaultedPaymentMethodAsync(string vaultId, PaymentSourceType sourceType, decimal amount, CancellationToken cancellationToken = default);
    }
}
