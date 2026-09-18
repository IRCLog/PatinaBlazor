using PatinaBlazor.Data;
using PatinaBlazor.Services.PayPal;

namespace PatinaBlazor.Tests.Fakes
{
    // Registered in place of the real PayPalClient for every Tier 1 test - CI has no PayPal
    // sandbox credentials, and IStoragePaymentService's orchestration logic (reservation,
    // charge/failure bookkeeping, status transitions) is what these tests actually cover,
    // not PayPal's own API. Each call is controllable per-test via the public fields, and
    // every call is recorded so a test can assert what was actually invoked (e.g. that the
    // right vault_id/amount/source type was charged).
    public class FakePayPalClient : IPayPalClient
    {
        public bool SetupTokenSucceeds { get; set; } = true;
        public string SetupTokenId { get; set; } = "FAKE-SETUP-TOKEN";
        public string ApproveUrl { get; set; } = "https://sandbox.paypal.com/agreements/approve?approval_session_id=FAKE";
        public string? SetupTokenError { get; set; }

        public bool ClientTokenSucceeds { get; set; } = true;
        public string ClientToken { get; set; } = "FAKE-CLIENT-TOKEN";
        public string? ClientTokenError { get; set; }

        public bool PaymentTokenSucceeds { get; set; } = true;
        public string PaymentTokenId { get; set; } = "FAKE-PAYMENT-TOKEN";
        public PaymentSourceType PaymentTokenSourceType { get; set; } = PaymentSourceType.PayPal;
        public string? DisplayLabel { get; set; } = "fake-payer@example.com";
        public string? PaymentTokenError { get; set; }

        public bool ChargeSucceeds { get; set; } = true;
        public string OrderId { get; set; } = "FAKE-ORDER-ID";
        public string? ChargeError { get; set; }

        public List<(string VaultId, PaymentSourceType SourceType, decimal Amount)> ChargeCalls { get; } = [];

        public Task<PayPalSetupTokenResult> CreateSetupTokenAsync(string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetupTokenSucceeds
                ? new PayPalSetupTokenResult { Succeeded = true, SetupTokenId = SetupTokenId, ApproveUrl = ApproveUrl }
                : new PayPalSetupTokenResult { Succeeded = false, Error = SetupTokenError ?? "Fake setup token failure." });
        }

        public Task<PayPalSetupTokenResult> CreateCardSetupTokenAsync(string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetupTokenSucceeds
                ? new PayPalSetupTokenResult { Succeeded = true, SetupTokenId = SetupTokenId }
                : new PayPalSetupTokenResult { Succeeded = false, Error = SetupTokenError ?? "Fake card setup token failure." });
        }

        public Task<PayPalClientTokenResult> GetBrowserSafeClientTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ClientTokenSucceeds
                ? new PayPalClientTokenResult { Succeeded = true, ClientToken = ClientToken }
                : new PayPalClientTokenResult { Succeeded = false, Error = ClientTokenError ?? "Fake client token failure." });
        }

        public Task<PayPalPaymentTokenResult> CreatePaymentTokenFromSetupTokenAsync(string setupTokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PaymentTokenSucceeds
                ? new PayPalPaymentTokenResult { Succeeded = true, PaymentTokenId = PaymentTokenId, SourceType = PaymentTokenSourceType, DisplayLabel = DisplayLabel }
                : new PayPalPaymentTokenResult { Succeeded = false, Error = PaymentTokenError ?? "Fake payment token failure." });
        }

        public Task<PayPalChargeResult> ChargeVaultedPaymentMethodAsync(string vaultId, PaymentSourceType sourceType, decimal amount, CancellationToken cancellationToken = default)
        {
            ChargeCalls.Add((vaultId, sourceType, amount));
            return Task.FromResult(ChargeSucceeds
                ? new PayPalChargeResult { Succeeded = true, OrderId = OrderId }
                : new PayPalChargeResult { Succeeded = false, Error = ChargeError ?? "Fake charge failure." });
        }
    }
}
