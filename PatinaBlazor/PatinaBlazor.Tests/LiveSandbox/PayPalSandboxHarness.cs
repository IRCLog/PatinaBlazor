using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PatinaBlazor.Services.PayPal;

namespace PatinaBlazor.Tests.LiveSandbox
{
    // Shared setup for the dev-only "RequiresPayPalSandbox" test suite - see this folder's
    // PayPalLiveSandboxTests.cs for the actual tests and why this category exists.
    //
    // Reads the real PayPal sandbox ClientId/ClientSecret from dotnet user-secrets (the SAME
    // secrets.json the main app reads - PatinaBlazor.Tests.csproj was given the same
    // UserSecretsId specifically for this). Throws a clear, actionable error rather than a
    // confusing downstream 401 if they're not configured on this machine.
    public class PayPalSandboxHarness
    {
        public IPayPalClient Client { get; }
        public string BaseUrl { get; }

        private readonly HttpClient _rawHttpClient;
        private readonly string _clientId;
        private readonly string _clientSecret;

        public PayPalSandboxHarness()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets(typeof(PayPalSandboxHarness).Assembly)
                .Build();

            _clientId = configuration["Paypal:ClientId"] ?? "";
            _clientSecret = configuration["Paypal:ClientSecret"] ?? "";
            BaseUrl = configuration["Paypal:BaseUrl"] is { Length: > 0 } configuredUrl ? configuredUrl : "https://api-m.sandbox.paypal.com";

            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            {
                throw new InvalidOperationException(
                    "No PayPal sandbox credentials found in user-secrets (Paypal:ClientId / Paypal:ClientSecret). " +
                    "These tests are dev-only and need real sandbox credentials configured via " +
                    "'dotnet user-secrets set' in the main PatinaBlazor project - see this folder's README comment.");
            }

            var options = Options.Create(new PayPalOptions { ClientId = _clientId, ClientSecret = _clientSecret, BaseUrl = BaseUrl, BrandName = "PatinaBlazor Live Sandbox Tests" });
            var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            Client = new PayPalClient(httpClient, options, new PayPalTokenCache(), NullLogger<PayPalClient>.Instance);

            // A second, raw HttpClient used only by VaultRawCardAsync below - that method
            // exists specifically because PayPalClient's own CreateCardSetupTokenAsync never
            // carries actual card data (in this app's real production flow, raw card data is
            // submitted separately, client-side, via PayPal's Card Fields JS SDK talking
            // directly to PayPal's GraphQL API - see wwwroot/js/paypalCardFields.js). To
            // fixture a real, already-vaulted card for testing PayPalClient's own
            // CreatePaymentTokenFromSetupTokenAsync/ChargeVaultedPaymentMethodAsync methods
            // (which DO take a real vault reference), something has to submit real card data
            // to PayPal first - this does that the same way the REST API itself allows,
            // confirmed live to work directly against this account.
            _rawHttpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        }

        // Creates a real setup token directly via REST WITH real card data attached in one
        // call (bypassing PayPalClient, per the class comment above) - confirmed live this is
        // the only REST-reachable way to get a card's actual data attached to a setup token:
        // the official Vault v3 OpenAPI spec exposes only GET on /v3/vault/setup-tokens/{id},
        // no PUT/PATCH, so there is no REST equivalent of the two-step "create empty, then
        // attach card data" pattern the browser's Card Fields JS SDK uses via PayPal's
        // internal GraphQL API (UpdateVaultSetupToken) - the card data has to be included in
        // the same request that creates the setup token when going through REST directly.
        // Returns the setup token id, ready to hand into the REAL
        // PayPalClient.CreatePaymentTokenFromSetupTokenAsync for the actual test under
        // exercise (finalizing it into a reusable vault_id).
        //
        // requireVerification mirrors this app's own real CreateCardSetupTokenAsync request
        // (verification_method: SCA_WHEN_REQUIRED) when true - confirmed live that PayPal's
        // negative-testing CCREJECT-* trigger names fail this $0 verification outright
        // (UNPROCESSABLE_ENTITY/INVALID_PAYMENT_SOURCE), so a card meant to vault successfully
        // but decline on a LATER real charge needs requireVerification:false to reach that
        // vaulted state at all.
        public async Task<string> CreateSetupTokenWithRawCardAsync(string cardholderName, string cardNumber, bool requireVerification)
        {
            var accessToken = await GetAccessTokenAsync();

            var cardBody = new JsonObject
            {
                ["name"] = cardholderName,
                ["number"] = cardNumber,
                ["expiry"] = "2030-01",
                ["security_code"] = "123",
                ["usage_type"] = "MERCHANT"
            };
            if (requireVerification)
            {
                cardBody["verification_method"] = "SCA_WHEN_REQUIRED";
                cardBody["experience_context"] = new JsonObject
                {
                    ["return_url"] = "https://example.com/return",
                    ["cancel_url"] = "https://example.com/cancel"
                };
            }

            var setupTokenResponse = await PostAsync("/v3/vault/setup-tokens", new JsonObject { ["payment_source"] = new JsonObject { ["card"] = cardBody } }, accessToken);
            return setupTokenResponse["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Vaulting setup-token creation did not return an id: {setupTokenResponse.ToJsonString()}");
        }

        private async Task<JsonNode> PostAsync(string path, JsonObject body, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("PayPal-Request-Id", Guid.NewGuid().ToString());

            using var response = await _rawHttpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"POST {path} failed ({(int)response.StatusCode}): {responseBody}");
            }

            return JsonNode.Parse(responseBody) ?? throw new InvalidOperationException($"POST {path} returned an unparseable body: {responseBody}");
        }

        private async Task<string> GetAccessTokenAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            using var response = await _rawHttpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            return JsonNode.Parse(responseBody)!["access_token"]!.GetValue<string>();
        }
    }
}
