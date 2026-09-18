using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services.PayPal
{
    public class PayPalClient : IPayPalClient
    {
        private readonly HttpClient _httpClient;
        private readonly PayPalOptions _options;
        private readonly PayPalTokenCache _tokenCache;
        private readonly ILogger<PayPalClient> _logger;

        public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, PayPalTokenCache tokenCache, ILogger<PayPalClient> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _tokenCache = tokenCache;
            _logger = logger;
        }

        public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            var body = new JsonObject
            {
                ["payment_source"] = new JsonObject
                {
                    ["paypal"] = new JsonObject
                    {
                        ["usage_pattern"] = "RECURRING_PREPAID",
                        ["usage_type"] = "MERCHANT",
                        ["customer_type"] = "CONSUMER",
                        ["permit_multiple_payment_tokens"] = false,
                        ["experience_context"] = new JsonObject
                        {
                            ["brand_name"] = _options.BrandName,
                            ["locale"] = "en-US",
                            ["return_url"] = returnUrl,
                            ["cancel_url"] = cancelUrl,
                            ["shipping_preference"] = "NO_SHIPPING"
                        }
                    }
                }
            };

            var (succeeded, json, error) = await PostAsync("/v3/vault/setup-tokens", body, cancellationToken);
            if (!succeeded)
            {
                return new PayPalSetupTokenResult { Succeeded = false, Error = error };
            }

            var setupTokenId = json!["id"]?.GetValue<string>();
            var approveUrl = json["links"]?.AsArray()
                .FirstOrDefault(link => link?["rel"]?.GetValue<string>() == "approve")?["href"]?.GetValue<string>();

            if (string.IsNullOrEmpty(setupTokenId) || string.IsNullOrEmpty(approveUrl))
            {
                _logger.LogError("PayPal setup-token response was missing an id or approve link: {Response}", json.ToJsonString());
                return new PayPalSetupTokenResult { Succeeded = false, Error = "PayPal did not return an approval link." };
            }

            return new PayPalSetupTokenResult { Succeeded = true, SetupTokenId = setupTokenId, ApproveUrl = approveUrl };
        }

        public async Task<PayPalSetupTokenResult> CreateCardSetupTokenAsync(string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            // Matches PayPal's own official Card Fields server sample exactly
            // (paypal-examples/v6-web-sdk-sample-integration/server/node - an earlier,
            // empty-card version of this request reproduced a real ERR_DEV_RECEIVED_
            // GRAPHQL_ERROR from the browser SDK's submit() call, root-caused by comparing
            // against this real working sample rather than guessing further).
            // verification_method opts the card into 3DS/SCA when the card issuer or local
            // regulations require it - confirmed via the real OpenAPI spec that return_url/
            // cancel_url become required the moment any "contingency flow like PayPal
            // wallet, 3DS" is possible, which SCA_WHEN_REQUIRED makes possible for a card
            // even though there is no PayPal-account login step involved.
            var body = new JsonObject
            {
                ["payment_source"] = new JsonObject
                {
                    ["card"] = new JsonObject
                    {
                        ["verification_method"] = "SCA_WHEN_REQUIRED",
                        ["usage_type"] = "MERCHANT",
                        ["experience_context"] = new JsonObject
                        {
                            ["return_url"] = returnUrl,
                            ["cancel_url"] = cancelUrl
                        }
                    }
                }
            };

            var (succeeded, json, error) = await PostAsync("/v3/vault/setup-tokens", body, cancellationToken);
            if (!succeeded)
            {
                return new PayPalSetupTokenResult { Succeeded = false, Error = error };
            }

            var setupTokenId = json!["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(setupTokenId))
            {
                _logger.LogError("PayPal card setup-token response was missing an id: {Response}", json.ToJsonString());
                return new PayPalSetupTokenResult { Succeeded = false, Error = "PayPal did not return a setup token." };
            }

            return new PayPalSetupTokenResult { Succeeded = true, SetupTokenId = setupTokenId };
        }

        public async Task<PayPalClientTokenResult> GetBrowserSafeClientTokenAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var clientToken = await _tokenCache.GetOrFetchClientTokenAsync(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
                    {
                        // domains[] deliberately omitted - PayPal rejects "localhost"/any
                        // unregistered host with invalid_domain for sandbox/local dev; only
                        // a real production deploy with registered origins would add it.
                        Content = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["grant_type"] = "client_credentials",
                            ["response_type"] = "client_token"
                        })
                    };
                    var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var json = JsonNode.Parse(responseBody)!;
                    // The client-safe token comes back as "access_token" even with
                    // response_type=client_token - PayPal never renames the field.
                    var token = json["access_token"]!.GetValue<string>();
                    var expiresIn = json["expires_in"]!.GetValue<int>();
                    return (token, TimeSpan.FromSeconds(expiresIn));
                });

                return new PayPalClientTokenResult { Succeeded = true, ClientToken = clientToken };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch a PayPal browser-safe client token.");
                return new PayPalClientTokenResult { Succeeded = false, Error = "We couldn't start card checkout right now. Please try again." };
            }
        }

        public async Task<PayPalPaymentTokenResult> CreatePaymentTokenFromSetupTokenAsync(string setupTokenId, CancellationToken cancellationToken = default)
        {
            var body = new JsonObject
            {
                ["payment_source"] = new JsonObject
                {
                    ["token"] = new JsonObject
                    {
                        ["id"] = setupTokenId,
                        ["type"] = "SETUP_TOKEN"
                    }
                }
            };

            var (succeeded, json, error) = await PostAsync("/v3/vault/payment-tokens", body, cancellationToken);
            if (!succeeded)
            {
                return new PayPalPaymentTokenResult { Succeeded = false, Error = error };
            }

            var paymentTokenId = json!["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(paymentTokenId))
            {
                _logger.LogError("PayPal payment-token response was missing an id: {Response}", json.ToJsonString());
                return new PayPalPaymentTokenResult { Succeeded = false, Error = "PayPal did not return a payment token." };
            }

            // Branches on which payment_source the response actually carries - a
            // card-sourced setup token's payment-token response has payment_source.card
            // (brand/last_digits), never payment_source.paypal, and vice versa.
            var paypalSource = json["payment_source"]?["paypal"];
            var cardSource = json["payment_source"]?["card"];
            string? displayLabel;
            var sourceType = PaymentSourceType.PayPal;
            if (paypalSource != null)
            {
                displayLabel = paypalSource["email_address"]?.GetValue<string>();
            }
            else if (cardSource != null)
            {
                sourceType = PaymentSourceType.Card;
                var brand = cardSource["brand"]?.GetValue<string>();
                var lastDigits = cardSource["last_digits"]?.GetValue<string>();
                displayLabel = !string.IsNullOrEmpty(lastDigits)
                    ? $"{brand ?? "Card"} ending in {lastDigits}"
                    : brand;
            }
            else
            {
                displayLabel = null;
            }

            return new PayPalPaymentTokenResult { Succeeded = true, PaymentTokenId = paymentTokenId, SourceType = sourceType, DisplayLabel = displayLabel };
        }

        public async Task<PayPalChargeResult> ChargeVaultedPaymentMethodAsync(string vaultId, PaymentSourceType sourceType, decimal amount, CancellationToken cancellationToken = default)
        {
            var sourceKey = sourceType == PaymentSourceType.Card ? "card" : "paypal";
            var body = new JsonObject
            {
                ["intent"] = "CAPTURE",
                ["purchase_units"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["amount"] = new JsonObject
                        {
                            ["currency_code"] = "USD",
                            ["value"] = amount.ToString("F2", CultureInfo.InvariantCulture)
                        }
                    }
                },
                ["payment_source"] = new JsonObject
                {
                    [sourceKey] = new JsonObject
                    {
                        ["vault_id"] = vaultId
                    }
                }
            };

            var (succeeded, json, error) = await PostAsync("/v2/checkout/orders", body, cancellationToken);
            if (!succeeded)
            {
                return new PayPalChargeResult { Succeeded = false, Error = error };
            }

            var orderId = json!["id"]?.GetValue<string>();
            var status = json["status"]?.GetValue<string>();

            if (status == "COMPLETED")
            {
                return new PayPalChargeResult { Succeeded = true, OrderId = orderId };
            }

            _logger.LogWarning("PayPal order {OrderId} did not complete synchronously, status {Status}", orderId, status);
            return new PayPalChargeResult
            {
                Succeeded = false,
                OrderId = orderId,
                Error = $"Payment did not complete (status: {status})."
            };
        }

        // A single choke point every public method funnels through, wrapped in a broad
        // try/catch on purpose: a thrown exception here (a network timeout, a DNS failure,
        // an OAuth2 token fetch rejected outright) would otherwise propagate up through a
        // Blazor Server event handler and crash the customer's entire circuit mid-checkout -
        // converting every real-world failure mode into an ordinary Succeeded=false result
        // is what lets the wizard/return page show a friendly message instead.
        private async Task<(bool Succeeded, JsonNode? Json, string? Error)> PostAsync(string path, JsonObject body, CancellationToken cancellationToken)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync(cancellationToken);

                using var request = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Add("PayPal-Request-Id", Guid.NewGuid().ToString());

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = ExtractErrorMessage(responseBody);
                    _logger.LogError("PayPal {Path} returned {StatusCode}: {Body}", path, (int)response.StatusCode, responseBody);
                    return (false, null, error);
                }

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    return (true, new JsonObject(), null);
                }

                return (true, JsonNode.Parse(responseBody), null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A timeout, not a real cancellation request - fall through to the generic
                // handler below rather than letting this specific case re-throw.
                _logger.LogError("PayPal {Path} timed out.", path);
                return (false, null, "PayPal did not respond in time. Please try again.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "PayPal {Path} threw an unexpected exception.", path);
                return (false, null, "We couldn't reach PayPal right now. Please try again.");
            }
        }

        private static string ExtractErrorMessage(string responseBody)
        {
            try
            {
                var json = JsonNode.Parse(responseBody);
                var message = json?["message"]?.GetValue<string>();
                var firstIssue = json?["details"]?.AsArray().FirstOrDefault()?["description"]?.GetValue<string>()
                    ?? json?["details"]?.AsArray().FirstOrDefault()?["issue"]?.GetValue<string>();
                return firstIssue ?? message ?? "PayPal returned an error.";
            }
            catch
            {
                return "PayPal returned an error.";
            }
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return await _tokenCache.GetOrFetchAccessTokenAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
                };
                var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = JsonNode.Parse(responseBody)!;
                var token = json["access_token"]!.GetValue<string>();
                var expiresIn = json["expires_in"]!.GetValue<int>();
                return (token, TimeSpan.FromSeconds(expiresIn));
            });
        }
    }
}
