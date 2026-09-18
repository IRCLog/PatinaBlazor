using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PatinaBlazor.Data;
using PatinaBlazor.Services.PayPal;
using PatinaBlazor.Tests.Fakes;

namespace PatinaBlazor.Tests
{
    // Covers PayPalClient's own handling of the real response/error shapes PayPal can send
    // back - the user's explicit request after this session's live-debugging work ("I think
    // having some unit tests covering all the statuses we could get back and how we handle
    // them is important"), rather than relying on browser-driven decline testing (which
    // sandbox's "must be a test card number" restriction made impractical - see the
    // 2026-09-16/17 checkpoint entries). No real network calls: PayPalClient is constructed
    // with a real HttpClient wrapping a FakeHttpMessageHandler, so PostAsync/ExtractErrorMessage/
    // each public method's response mapping all run for real, just against canned responses.
    //
    // Error-shape fixtures below are modeled on PayPal's real, documented error format
    // (name/message/details[]/debug_id/links) and on the actual shapes captured live during
    // this session's debugging (see PayPalClient.cs's own comments and the paypalCardFields.js
    // _describeError tests) - not invented arbitrarily.
    public class PayPalClientTests
    {
        private static PayPalClient CreateClient(FakeHttpMessageHandler handler)
        {
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api-m.sandbox.paypal.com") };
            var options = Options.Create(new PayPalOptions
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                BrandName = "PatinaBlazor Test"
            });
            // A fresh cache per test - it's stateful (caches the fetched token), and tests
            // must not leak a cached token from one FakeHttpMessageHandler into another.
            var tokenCache = new PayPalTokenCache();
            return new PayPalClient(httpClient, options, tokenCache, NullLogger<PayPalClient>.Instance);
        }

        [Fact]
        public async Task CreateSetupTokenAsync_Success_ReturnsSetupTokenIdAndApproveUrl()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.Created, """
                {"id":"SETUP-TOKEN-1","links":[{"rel":"approve","href":"https://sandbox.paypal.com/approve/SETUP-TOKEN-1"},{"rel":"self","href":"https://api.paypal.com/v3/vault/setup-tokens/SETUP-TOKEN-1"}]}
                """);
            var client = CreateClient(handler);

            var result = await client.CreateSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("SETUP-TOKEN-1", result.SetupTokenId);
            Assert.Equal("https://sandbox.paypal.com/approve/SETUP-TOKEN-1", result.ApproveUrl);
        }

        [Fact]
        public async Task CreateSetupTokenAsync_ResponseMissingApproveLink_ReturnsFriendlyError()
        {
            var handler = new FakeHttpMessageHandler();
            // A malformed-but-200 response - no "approve" link in the links array at all.
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.Created, """{"id":"SETUP-TOKEN-2","links":[{"rel":"self","href":"https://api.paypal.com/x"}]}""");
            var client = CreateClient(handler);

            var result = await client.CreateSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("PayPal did not return an approval link.", result.Error);
        }

        [Fact]
        public async Task CreateCardSetupTokenAsync_Success_ReturnsSetupTokenIdWithNoApproveUrlNeeded()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.Created, """{"id":"CARD-SETUP-TOKEN-1"}""");
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("CARD-SETUP-TOKEN-1", result.SetupTokenId);
        }

        [Fact]
        public async Task ValidationError_WithDetailsDescription_UsesTheDescriptionAsTheFriendlyError()
        {
            // The real shape behind the "sandbox rejects a non-test card number" error hit
            // live during this session's debugging.
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.UnprocessableEntity, """
                {"name":"UNPROCESSABLE_ENTITY","message":"The requested action could not be performed, semantically incorrect, or failed business validation.",
                 "debug_id":"abc123","details":[{"field":"/payment_source/card/number","location":"body","issue":"CREDIT_CARD_NUMBER_MUST_BE_TEST_NUMBER","description":"Credit card number is not an accepted test number."}],
                 "links":[{"href":"https://developer.paypal.com/docs/api/orders/v2/#error-CREDIT_CARD_NUMBER_MUST_BE_TEST_NUMBER","rel":"information_link"}]}
                """);
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("Credit card number is not an accepted test number.", result.Error);
        }

        [Fact]
        public async Task ValidationError_DetailsHasIssueButNoDescription_FallsBackToTheIssueCode()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.BadRequest, """
                {"name":"INVALID_REQUEST","message":"Request is not well-formed, syntactically incorrect, or violates schema.",
                 "details":[{"field":"/payment_source/card/expiry","location":"body","issue":"INVALID_PARAMETER_SYNTAX"}]}
                """);
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("INVALID_PARAMETER_SYNTAX", result.Error);
        }

        [Fact]
        public async Task Error_NoDetailsArrayAtAll_FallsBackToTopLevelMessage()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.Unauthorized, """
                {"name":"AUTHENTICATION_FAILURE","message":"Authentication failed due to invalid authentication credentials or a missing Authorization header."}
                """);
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("Authentication failed due to invalid authentication credentials or a missing Authorization header.", result.Error);
        }

        [Fact]
        public async Task Error_UnparseableResponseBody_ReturnsGenericFallbackWithoutThrowing()
        {
            var handler = new FakeHttpMessageHandler();
            // Not JSON at all - e.g. an upstream proxy/WAF error page.
            handler.On("/v3/vault/setup-tokens", HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>");
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("PayPal returned an error.", result.Error);
        }

        [Fact]
        public async Task Error_EmptyErrorBody_ReturnsGenericFallback()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/setup-tokens", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("PayPal returned an error.", result.Error);
        }

        [Fact]
        public async Task NetworkFailure_ThrownExceptionMidRequest_ConvertsToFriendlyFailureNotAnException()
        {
            // PostAsync's broad try/catch exists specifically so a real network failure never
            // propagates up through a Blazor Server event handler and crashes the customer's
            // circuit - this proves that safety net actually works, not just that it exists.
            var handler = new FakeHttpMessageHandler();
            handler.Throw("/v3/vault/setup-tokens", new HttpRequestException("Connection reset by peer"));
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("We couldn't reach PayPal right now. Please try again.", result.Error);
        }

        [Fact]
        public async Task NetworkFailure_Timeout_ConvertsToFriendlyTimeoutMessage()
        {
            var handler = new FakeHttpMessageHandler();
            // TaskCanceledException with the ambient (never-cancelled) token simulates an
            // HttpClient-internal timeout, not a real caller-requested cancellation - PostAsync
            // deliberately distinguishes the two via `when (!cancellationToken.IsCancellationRequested)`.
            handler.Throw("/v3/vault/setup-tokens", new TaskCanceledException("The request timed out."));
            var client = CreateClient(handler);

            var result = await client.CreateCardSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("PayPal did not respond in time. Please try again.", result.Error);
        }

        [Fact]
        public async Task OAuth2TokenFetchFails_PropagatesAsAFriendlyFailureOnTheFirstRealCall()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v1/oauth2/token", HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"Client Authentication failed"}""");
            var client = CreateClient(handler);

            var result = await client.CreateSetupTokenAsync("https://example.com/return", "https://example.com/cancel");

            Assert.False(result.Succeeded);
            Assert.Equal("We couldn't reach PayPal right now. Please try again.", result.Error);
        }

        [Fact]
        public async Task GetBrowserSafeClientTokenAsync_Success_ReturnsTheToken()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v1/oauth2/token", HttpStatusCode.OK, """{"access_token":"BROWSER-SAFE-TOKEN","expires_in":32400}""");
            var client = CreateClient(handler);

            var result = await client.GetBrowserSafeClientTokenAsync();

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("BROWSER-SAFE-TOKEN", result.ClientToken);
        }

        [Fact]
        public async Task GetBrowserSafeClientTokenAsync_Failure_ReturnsFriendlyMessageNotTheRawError()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v1/oauth2/token", HttpStatusCode.BadRequest, """{"error":"unsupported_grant_type"}""");
            var client = CreateClient(handler);

            var result = await client.GetBrowserSafeClientTokenAsync();

            Assert.False(result.Succeeded);
            Assert.Equal("We couldn't start card checkout right now. Please try again.", result.Error);
        }

        [Fact]
        public async Task CreatePaymentTokenFromSetupTokenAsync_PayPalSource_ExtractsEmailAndSourceType()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/payment-tokens", HttpStatusCode.Created, """
                {"id":"PAYMENT-TOKEN-1","payment_source":{"paypal":{"email_address":"customer@example.com"}}}
                """);
            var client = CreateClient(handler);

            var result = await client.CreatePaymentTokenFromSetupTokenAsync("SETUP-1");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("PAYMENT-TOKEN-1", result.PaymentTokenId);
            Assert.Equal(PaymentSourceType.PayPal, result.SourceType);
            Assert.Equal("customer@example.com", result.DisplayLabel);
        }

        [Fact]
        public async Task CreatePaymentTokenFromSetupTokenAsync_CardSource_ExtractsBrandAndLastDigitsAndSourceType()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/payment-tokens", HttpStatusCode.Created, """
                {"id":"PAYMENT-TOKEN-2","payment_source":{"card":{"brand":"VISA","last_digits":"4242","expiry":"2030-01"}}}
                """);
            var client = CreateClient(handler);

            var result = await client.CreatePaymentTokenFromSetupTokenAsync("SETUP-2");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(PaymentSourceType.Card, result.SourceType);
            Assert.Equal("VISA ending in 4242", result.DisplayLabel);
        }

        [Fact]
        public async Task CreatePaymentTokenFromSetupTokenAsync_CardSourceWithNoLastDigits_FallsBackToBrandOnly()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v3/vault/payment-tokens", HttpStatusCode.Created, """
                {"id":"PAYMENT-TOKEN-3","payment_source":{"card":{"brand":"VISA"}}}
                """);
            var client = CreateClient(handler);

            var result = await client.CreatePaymentTokenFromSetupTokenAsync("SETUP-3");

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("VISA", result.DisplayLabel);
        }

        [Fact]
        public async Task ChargeVaultedPaymentMethodAsync_StatusCompleted_Succeeds()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.Created, """{"id":"ORDER-1","status":"COMPLETED"}""");
            var client = CreateClient(handler);

            var result = await client.ChargeVaultedPaymentMethodAsync("VAULT-1", PaymentSourceType.PayPal, 110m);

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("ORDER-1", result.OrderId);
        }

        [Theory]
        [InlineData("PAYER_ACTION_REQUIRED")]
        [InlineData("VOIDED")]
        [InlineData("PENDING")]
        public async Task ChargeVaultedPaymentMethodAsync_StatusNotCompleted_TreatedAsFailureWithTheStatusInTheMessage(string status)
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.Created, $$"""{"id":"ORDER-2","status":"{{status}}"}""");
            var client = CreateClient(handler);

            var result = await client.ChargeVaultedPaymentMethodAsync("VAULT-1", PaymentSourceType.PayPal, 110m);

            Assert.False(result.Succeeded);
            Assert.Equal("ORDER-2", result.OrderId);
            Assert.Contains(status, result.Error);
        }

        [Fact]
        public async Task ChargeVaultedPaymentMethodAsync_Declined_ReturnsFriendlyDeclineMessage()
        {
            // PayPal's own documented UNPROCESSABLE_ENTITY / INSTRUMENT_DECLINED shape (real,
            // confirmed via PayPal's own developer docs - see the JS SDK error-handling
            // example in their security/sandbox-testing guide). This app's own live sandbox
            // testing of PayPal's CCREJECT-* negative-testing triggers, charged via vault_id
            // (this app's actual real request shape), consistently produced a different real
            // issue code instead - PAYER_CANNOT_PAY, see
            // ChargeVaultedPaymentMethodAsync_PayerCannotPay_RealCapturedSandboxShape_ReturnsFriendlyDeclineMessage
            // below for that live-captured case. Both are real, documented PayPal error
            // codes for the same general "instrument declined" family; kept as two separate
            // tests since ExtractErrorMessage's `details[0].description` handling is identical
            // either way and both are worth having on record.
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.UnprocessableEntity, """
                {"name":"UNPROCESSABLE_ENTITY","message":"The requested action could not be performed, semantically incorrect, or failed business validation.",
                 "details":[{"issue":"INSTRUMENT_DECLINED","description":"The instrument presented either failed authentication or is invalid."}]}
                """);
            var client = CreateClient(handler);

            var result = await client.ChargeVaultedPaymentMethodAsync("VAULT-1", PaymentSourceType.Card, 110m);

            Assert.False(result.Succeeded);
            Assert.Equal("The instrument presented either failed authentication or is invalid.", result.Error);
        }

        [Fact]
        public async Task ChargeVaultedPaymentMethodAsync_PayerCannotPay_RealCapturedSandboxShape_ReturnsFriendlyDeclineMessage()
        {
            // The REAL response captured live against the actual PayPal sandbox: charging a
            // card vaulted (via the real /v3/vault/setup-tokens + /v3/vault/payment-tokens
            // flow) with PayPal's CCREJECT-IF/CCREJECT-REFUSED negative-testing trigger names,
            // via vault_id (this app's real ChargeVaultedPaymentMethodAsync request shape),
            // returns this exact 422 PAYER_CANNOT_PAY body - not INSTRUMENT_DECLINED. This is
            // the decline shape this app's real production charge path actually hits when a
            // sandbox test card is configured to decline, confirmed by direct curl/HttpClient
            // testing against the live sandbox (see the dev-only PayPalLiveSandboxTests, which
            // exercise this same scenario against the real API rather than a canned fixture).
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.UnprocessableEntity, """
                {"name":"UNPROCESSABLE_ENTITY","details":[{"location":"body","issue":"PAYER_CANNOT_PAY","description":"Payer cannot pay for this transaction. Please contact the payer to find other ways to pay for this transaction."}],
                 "message":"The requested action could not be performed, semantically incorrect, or failed business validation.","debug_id":"ca3cde4c80cb2"}
                """);
            var client = CreateClient(handler);

            var result = await client.ChargeVaultedPaymentMethodAsync("VAULT-1", PaymentSourceType.Card, 10m);

            Assert.False(result.Succeeded);
            Assert.Equal("Payer cannot pay for this transaction. Please contact the payer to find other ways to pay for this transaction.", result.Error);
        }

        [Fact]
        public async Task ChargeVaultedPaymentMethodAsync_OrderCompletedButCaptureDeclined_RealCapturedSandboxShape_TreatedAsAFailureNotASuccess()
        {
            // The other real shape captured live: charging with an INLINE card (not vault_id)
            // carrying a CCREJECT-IF/CCREJECT-REFUSED trigger name returns HTTP 200/201 with
            // the ORDER's own top-level status:"COMPLETED" - while the actual capture inside
            // it has status:"DECLINED". This app never charges with inline card data (always
            // vault_id, which produces the PAYER_CANNOT_PAY shape above instead - confirmed
            // live, see that test and PayPalClient.cs's own comment on this method), so this
            // specific shape isn't known to be reachable through this app's real request
            // pattern today - but the fix (checking the capture's own status, not just the
            // order's) is real and worth permanently guarding regardless, since relying on
            // "we've only ever observed the other shape" isn't a hard guarantee for every
            // decline reason/card network/account configuration.
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.Created, """
                {"id":"ORDER-CAPTURE-DECLINED","status":"COMPLETED","purchase_units":[{"reference_id":"default","payments":{"captures":[
                    {"id":"CAPTURE-1","status":"DECLINED","amount":{"currency_code":"USD","value":"10.00"},
                     "processor_response":{"avs_code":"G","cvv_code":"P","response_code":"5120"}}
                ]}}]}
                """);
            var client = CreateClient(handler);

            var result = await client.ChargeVaultedPaymentMethodAsync("VAULT-1", PaymentSourceType.Card, 10m);

            Assert.False(result.Succeeded);
            Assert.Equal("ORDER-CAPTURE-DECLINED", result.OrderId);
            Assert.Equal("The payment was declined by the card issuer.", result.Error);
        }

        [Fact]
        public async Task ChargeVaultedPaymentMethodAsync_PayPalSourceType_SendsPaypalVaultIdInTheRequestBody()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.Created, """{"id":"ORDER-3","status":"COMPLETED"}""");
            var client = CreateClient(handler);

            await client.ChargeVaultedPaymentMethodAsync("VAULT-PAYPAL-1", PaymentSourceType.PayPal, 42.50m);

            var (_, body) = handler.Requests.Single(r => r.Path == "/v2/checkout/orders");
            var json = JsonNode.Parse(body)!;
            Assert.Equal("VAULT-PAYPAL-1", json["payment_source"]!["paypal"]!["vault_id"]!.GetValue<string>());
            Assert.Null(json["payment_source"]!["card"]);
            Assert.Equal("42.50", json["purchase_units"]![0]!["amount"]!["value"]!.GetValue<string>());
        }

        [Fact]
        public async Task ChargeVaultedPaymentMethodAsync_CardSourceType_SendsCardVaultIdInTheRequestBody()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/v2/checkout/orders", HttpStatusCode.Created, """{"id":"ORDER-4","status":"COMPLETED"}""");
            var client = CreateClient(handler);

            await client.ChargeVaultedPaymentMethodAsync("VAULT-CARD-1", PaymentSourceType.Card, 42.50m);

            var (_, body) = handler.Requests.Single(r => r.Path == "/v2/checkout/orders");
            var json = JsonNode.Parse(body)!;
            Assert.Equal("VAULT-CARD-1", json["payment_source"]!["card"]!["vault_id"]!.GetValue<string>());
            Assert.Null(json["payment_source"]!["paypal"]);
        }
    }
}
