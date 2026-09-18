using Microsoft.Playwright;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Covers the Payment step's PayPal-adjacent surfaces in ways that don't require a real
    // PayPal sandbox login (WebAppFixture deliberately configures invalid PayPal credentials
    // for this whole collection - see its comment - so these are the two things Tier 2 CAN
    // verify for real without a human clicking through PayPal's hosted approval page: the
    // wizard degrades gracefully instead of crashing when PayPal rejects a call, and the
    // return page (/storage/signup/complete) handles every input-state branch that doesn't
    // require a genuinely-approved token. The actual approve -> finalize -> charge loop was
    // verified once, manually, against the real sandbox during implementation (see the
    // 2026-09-16 checkpoint entry) - not something CI can automate.
    [Collection("E2E")]
    public class StorageSignUpPaymentTests
    {
        private readonly WebAppFixture _fixture;

        public StorageSignUpPaymentTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ContinueToPayPal_PayPalRejectsTheCall_ShowsAFriendlyErrorInsteadOfCrashingTheCircuit()
        {
            var email = $"e2e-paypalfail-{Guid.NewGuid():N}@example.com";
            const string password = "E2eTest123!";

            await using var context = await _fixture.NewContextAsync();
            var wizardPage = await context.NewPageAsync();
            var confirmationLink = await SignUpAndConfirmAsync(context, wizardPage, email, password);
            await ClickRealConfirmationLinkInASeparateTabAsync(context, confirmationLink);

            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Select" }).First.ClickAsync();
            await wizardPage.GetByText("Billing Frequency").WaitForAsync(new() { Timeout = 10_000 });

            // The Payment step now offers a PayPal-or-Card choice before either flow's own
            // UI appears (the card-entry addition) - "Pay with PayPal" reveals the existing
            // "Continue to PayPal" button rather than that button being immediately visible.
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Pay with PayPal" }).ClickAsync();
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Continue to PayPal" }).ClickAsync();

            // A real HTTP 401 comes back from PayPal's own OAuth2 endpoint for these invalid
            // credentials - PayPalClient's exception-safety wrapper must turn that into an
            // ordinary Succeeded=false result, not let anything throw through the event
            // handler (which would crash the whole Blazor Server circuit).
            await wizardPage.GetByText("Please try again", new() { Exact = false }).WaitForAsync(new() { Timeout = 10_000 });

            // Still on the wizard page, still interactive - not a dead circuit.
            Assert.Contains("/storage/signup", wizardPage.Url);
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Continue to PayPal" }).WaitForAsync(new() { Timeout = 5_000 });
        }

        [Fact]
        public async Task PayWithCard_PayPalRejectsTheCall_ShowsAFriendlyErrorInsteadOfCrashingTheCircuit()
        {
            var email = $"e2e-cardfail-{Guid.NewGuid():N}@example.com";
            const string password = "E2eTest123!";

            await using var context = await _fixture.NewContextAsync();
            var wizardPage = await context.NewPageAsync();
            var confirmationLink = await SignUpAndConfirmAsync(context, wizardPage, email, password);
            await ClickRealConfirmationLinkInASeparateTabAsync(context, confirmationLink);

            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Select" }).First.ClickAsync();
            await wizardPage.GetByText("Billing Frequency").WaitForAsync(new() { Timeout = 10_000 });

            // Choosing Card calls StartCardSetupAsync server-side (GetBrowserSafeClientTokenAsync
            // + CreateCardSetupTokenAsync) before any client-side PayPal JS SDK is ever loaded -
            // with these deliberately-invalid credentials it fails at that server call, so this
            // never depends on real network access to PayPal's SDK host to prove the resilience.
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Pay with Card" }).ClickAsync();

            await wizardPage.GetByText("Please try again", new() { Exact = false }).WaitForAsync(new() { Timeout = 10_000 });

            // Still on the wizard page, still interactive - not a dead circuit.
            Assert.Contains("/storage/signup", wizardPage.Url);
            await wizardPage.GetByText("Billing Frequency").WaitForAsync(new() { Timeout = 5_000 });
        }

        [Fact]
        public async Task CompletePage_NoApprovalTokenInQueryString_ShowsCancelledMessage()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/storage/signup/complete?rentalId=1");

            await page.GetByText("Payment setup was cancelled").WaitForAsync(new() { Timeout = 10_000 });
        }

        [Fact]
        public async Task CompletePage_NoRentalIdInQueryString_ShowsAGenericNotFoundMessage()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/storage/signup/complete");

            await page.GetByText("We couldn't find your reservation").WaitForAsync(new() { Timeout = 10_000 });
        }

        [Fact]
        public async Task CompletePage_ApprovalTokenPresentButRentalDoesNotExist_ShowsAFriendlyErrorNotAServerException()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            // A syntactically real-looking approval_token_id, but no rental with this id
            // exists - CompleteVaultSetupAsync must fail cleanly (rental lookup fails first,
            // before ever calling PayPal), not throw.
            await page.GotoAsync($"{_fixture.BaseUrl}/storage/signup/complete?rentalId=999999&approval_token_id=FAKE-TOKEN");

            await page.GetByText("We couldn't finish setting up your payment").WaitForAsync(new() { Timeout = 10_000 });
            await page.GetByText("Rental not found", new() { Exact = false }).WaitForAsync(new() { Timeout = 5_000 });
        }

        private async Task<string> SignUpAndConfirmAsync(IBrowserContext context, IPage wizardPage, string email, string password)
        {
            await wizardPage.GotoAsync($"{_fixture.BaseUrl}/storage/signup");
            await wizardPage.GetByPlaceholder("name@example.com").FillAsync(email);
            await wizardPage.GetByPlaceholder("First name").FillAsync("Casey");
            await wizardPage.GetByPlaceholder("Last name").FillAsync($"E2E{Guid.NewGuid():N}"[..10]);
            await wizardPage.GetByPlaceholder("password", new() { Exact = true }).FillAsync(password);
            await wizardPage.GetByPlaceholder("confirm password").FillAsync(password);
            await wizardPage.GetByPlaceholder("Phone number").FillAsync("555-0123");
            await wizardPage.GetByPlaceholder("Address line 1").FillAsync("789 Test Ave");
            await wizardPage.GetByPlaceholder("City").FillAsync("Testville");
            await wizardPage.GetByPlaceholder("State").FillAsync("CA");
            await wizardPage.GetByPlaceholder("Postal code").FillAsync("90001");
            await wizardPage.GetByPlaceholder("Emergency contact name").FillAsync("Sam Backup");
            await wizardPage.GetByPlaceholder("Emergency contact phone").FillAsync("555-0199");
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

            await wizardPage.GetByText("Check your email").WaitForAsync(new() { Timeout = 10_000 });

            return await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ConfirmEmail", TimeSpan.FromSeconds(20));
        }

        private static async Task ClickRealConfirmationLinkInASeparateTabAsync(IBrowserContext context, string confirmationLink)
        {
            var confirmPage = await context.NewPageAsync();
            await confirmPage.GotoAsync(confirmationLink);
            await confirmPage.GetByText("Thank you for confirming your email").WaitForAsync(new() { Timeout = 10_000 });
            await confirmPage.CloseAsync();
        }
    }
}
