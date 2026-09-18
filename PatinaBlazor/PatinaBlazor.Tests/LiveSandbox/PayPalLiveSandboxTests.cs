using PatinaBlazor.Data;
using Xunit;

namespace PatinaBlazor.Tests.LiveSandbox
{
    // Dev-only tests: real, live HTTP calls against PayPal's actual sandbox API, through
    // PayPalClient's own real production methods - not canned fixtures. Complements
    // PayPalClientTests.cs (which proves "if PayPal sends us shape X, we handle it
    // correctly" against shapes we constructed from documentation/prior captures) by proving
    // "PayPal actually sends us shape X for real" and that this app's own parsing code
    // genuinely interprets the live response correctly, for every response type reachable
    // through PayPalClient's REST-level surface (add-card setup-token creation, PayPal
    // Wallet setup-token creation, vault finalize, and charge - both success and decline).
    //
    // Deliberately excluded from CI (tagged "RequiresPayPalSandbox"; the CI workflow filters
    // this category out of the Tier 1 job) - these need real PayPal sandbox credentials
    // (dotnet user-secrets, see PayPalSandboxHarness.cs), make real network calls to a
    // third-party service, and are meaningfully slower than the rest of Tier 1's fast,
    // fully-offline suite. Run them explicitly on a dev machine that has those credentials:
    //   dotnet test PatinaBlazor.Tests --filter "Category=RequiresPayPalSandbox"
    //
    // Real, documented findings from building this suite (2026-09-18), each captured live
    // against this account rather than assumed:
    //   - PayPal's CCREJECT-* negative-testing trigger names (in the cardholder-name field)
    //     work via direct REST calls too, not only through the hosted Card Fields UI as
    //     earlier sessions concluded - that earlier conclusion was a false negative caused by
    //     testing with a card number that fails its own Luhn checksum, not a real account/API
    //     limitation. 4012000033330026 (Visa) is a real, Luhn-valid, PayPal-documented test
    //     number confirmed to work for both vaulting and charging on this account.
    //   - There is no REST equivalent of the browser SDK's two-step "create an empty setup
    //     token, then attach real card data via GraphQL" pattern - the official Vault v3
    //     OpenAPI spec exposes only GET on /v3/vault/setup-tokens/{id}. Fixturing a real
    //     vaulted card over REST means including the card data in the SAME request that
    //     creates the setup token (see PayPalSandboxHarness.CreateSetupTokenWithRawCardAsync).
    //   - Vaulting a card WITH verification_method:SCA_WHEN_REQUIRED (this app's own real
    //     CreateCardSetupTokenAsync request shape) makes a CCREJECT-* card fail outright at
    //     the $0 verification step (UNPROCESSABLE_ENTITY/INVALID_PAYMENT_SOURCE) - it never
    //     gets vaulted at all. Vaulting the SAME card without verification lets it vault
    //     successfully, only declining later at charge time - used below specifically to
    //     reach that later-decline scenario, which PayPalClient.ChargeVaultedPaymentMethodAsync
    //     needs to handle correctly.
    //   - Charging via vault_id (this app's real ChargeVaultedPaymentMethodAsync request
    //     shape) can produce EITHER of two real decline shapes for the identical CCREJECT-*
    //     trigger, depending on the specific card BIN charged - confirmed live with two
    //     different real PayPal-documented test numbers: a clean top-level 422
    //     PAYER_CANNOT_PAY error for one BIN, vs. the order's own top-level status:"COMPLETED"
    //     with the capture inside it at status:"DECLINED" for another. This means the
    //     "order COMPLETED but capture DECLINED" gap ChargeVaultedPaymentMethodAsync's fix
    //     closes (see PayPalClientTests.cs's ChargeVaultedPaymentMethodAsync_
    //     OrderCompletedButCaptureDeclined_... fixture test, and PayPalClient.cs's own comment
    //     on this method) is genuinely reachable through this app's real request pattern, not
    //     just a defensive guard against a shape that could never actually occur.
    [Trait("Category", "RequiresPayPalSandbox")]
    public class PayPalLiveSandboxTests
    {
        // A real, Luhn-valid PayPal sandbox test card (Visa) - confirmed live to vault and
        // charge successfully on this account, distinct from an earlier, incorrect
        // "4032039885296700" noted in this project's history that turned out to fail its own
        // Luhn checksum and was never actually a valid test number to begin with.
        //
        // Real gotcha hit while building this suite, worth remembering: a DIFFERENT valid
        // test number ("4012000033330026") that verified successfully several times in a row
        // started failing every subsequent $0 verification with the exact same
        // UNPROCESSABLE_ENTITY/INVALID_PAYMENT_SOURCE shape a genuinely bad card produces -
        // for a perfectly ordinary cardholder name, not a CCREJECT-* trigger. PayPal's own
        // sandbox docs note this directly: "generate fresh [test numbers] in the dashboard's
        // Card Testing tool if a card is declined" - real-time verification against a test
        // number appears to have some kind of reuse/rate limit on this account. If this
        // number ever starts failing the same way, swap in a fresh one from that dashboard
        // tool rather than assuming CreateSetupTokenWithRawCardAsync/PayPalClient itself
        // broke.
        private const string ValidTestCardNumber = "4005519200000004";

        [Fact]
        public async Task FullCardVaultAndChargeFlow_ValidCard_SucceedsAtEveryStepWithRealParsedData()
        {
            var harness = new PayPalSandboxHarness();

            var setupTokenId = await harness.CreateSetupTokenWithRawCardAsync("Jane Sandbox", ValidTestCardNumber, requireVerification: true);

            var finalizeResult = await harness.Client.CreatePaymentTokenFromSetupTokenAsync(setupTokenId);
            Assert.True(finalizeResult.Succeeded, finalizeResult.Error);
            Assert.Equal(PaymentSourceType.Card, finalizeResult.SourceType);
            Assert.NotNull(finalizeResult.DisplayLabel);
            Assert.Contains("0004", finalizeResult.DisplayLabel); // last 4 digits of ValidTestCardNumber

            var chargeResult = await harness.Client.ChargeVaultedPaymentMethodAsync(finalizeResult.PaymentTokenId!, PaymentSourceType.Card, 12.34m);

            Assert.True(chargeResult.Succeeded, chargeResult.Error);
            Assert.False(string.IsNullOrEmpty(chargeResult.OrderId));
        }

        [Theory]
        [InlineData("CCREJECT-IF")]  // insufficient funds
        [InlineData("CCREJECT-EC")]  // expired card
        public async Task ChargeVaultedPaymentMethodAsync_RealDeclineTriggerCard_ReturnsAFriendlyMessageNotRawJson(string declineTrigger)
        {
            var harness = new PayPalSandboxHarness();

            // Vaulted WITHOUT verification, deliberately - a CCREJECT-* card fails outright at
            // the $0 verification step this app's real vaulting flow requires (confirmed live,
            // see the class-level comment), so reaching a later charge-time decline needs the
            // card to actually be vaulted first.
            var setupTokenId = await harness.CreateSetupTokenWithRawCardAsync(declineTrigger, ValidTestCardNumber, requireVerification: false);
            var finalizeResult = await harness.Client.CreatePaymentTokenFromSetupTokenAsync(setupTokenId);
            Assert.True(finalizeResult.Succeeded, finalizeResult.Error);

            var chargeResult = await harness.Client.ChargeVaultedPaymentMethodAsync(finalizeResult.PaymentTokenId!, PaymentSourceType.Card, 10.00m);

            Assert.False(chargeResult.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(chargeResult.Error));
            // The real, live failure text - never raw JSON dumped to the customer. Which of
            // the two real shapes PayPal actually returns for a given CCREJECT-* trigger
            // depends on the specific card BIN charged (confirmed live - see the class-level
            // comment and PayPalClient.cs's own comment on ChargeVaultedPaymentMethodAsync),
            // so this only asserts both real, possible friendly messages are handled - not
            // which one shows up for this particular test card today.
            Assert.DoesNotContain("{", chargeResult.Error);
            var isRecognizedDeclineMessage =
                chargeResult.Error.Contains("cannot pay", StringComparison.OrdinalIgnoreCase) ||
                chargeResult.Error.Contains("declined", StringComparison.OrdinalIgnoreCase);
            Assert.True(isRecognizedDeclineMessage, $"Unexpected decline message: {chargeResult.Error}");
        }

        [Fact]
        public async Task GetBrowserSafeClientTokenAsync_RealSandboxCall_Succeeds()
        {
            var harness = new PayPalSandboxHarness();

            var result = await harness.Client.GetBrowserSafeClientTokenAsync();

            Assert.True(result.Succeeded, result.Error);
            Assert.False(string.IsNullOrEmpty(result.ClientToken));
        }

        [Fact]
        public async Task CreateCardSetupTokenAsync_RealSandboxCall_Succeeds()
        {
            // This app's own real "add card" request shape (no card data yet - that's
            // submitted separately, client-side, via Card Fields - see the class comment) -
            // confirms it still gets a genuine 201 from PayPal's real API, catching any future
            // breaking change to this request/response shape that a fixture-only test never
            // could.
            var harness = new PayPalSandboxHarness();

            var result = await harness.Client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.True(result.Succeeded, result.Error);
            Assert.False(string.IsNullOrEmpty(result.SetupTokenId));
        }

        [Fact]
        public async Task CreateSetupTokenAsync_PayPalWalletFlow_RealSandboxCall_ReturnsAnApproveUrl()
        {
            var harness = new PayPalSandboxHarness();

            var result = await harness.Client.CreateSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.True(result.Succeeded, result.Error);
            Assert.False(string.IsNullOrEmpty(result.SetupTokenId));
            Assert.StartsWith("https://www.sandbox.paypal.com/", result.ApproveUrl);
        }
    }
}
