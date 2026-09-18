using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Permanent coverage for wwwroot/js/paypalCardFields.js's _describeError() - the function
    // that decides what a customer sees (and what gets logged server-side) when PayPal's Card
    // Fields SDK fails to save a card. Runs the REAL script file in a real Chromium JS engine
    // (via BrowserOnlyFixture, no app/DB needed - this is pure client-side logic), against the
    // real shapes captured live during this session's production debugging (see the "Errors
    // being returned from paypal" checkpoint entries) plus PayPal's documented error format.
    //
    // This replaces the several one-off, since-deleted Playwright console harnesses used to
    // verify each fix live during that debugging - per the user's explicit request, this
    // coverage needed to become permanent rather than re-verified by hand every time.
    [Collection("BrowserOnly")]
    public class PayPalCardFieldsErrorHandlingTests
    {
        private readonly BrowserOnlyFixture _fixture;

        public PayPalCardFieldsErrorHandlingTests(BrowserOnlyFixture fixture)
        {
            _fixture = fixture;
        }

        private static readonly string ScriptSource = LoadScriptSource();

        private static string LoadScriptSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PatinaBlazor.sln")))
            {
                dir = dir.Parent;
            }
            if (dir is null)
            {
                throw new InvalidOperationException("Could not locate PatinaBlazor.sln above the test output directory.");
            }

            var scriptPath = Path.Combine(dir.FullName, "PatinaBlazor", "wwwroot", "js", "paypalCardFields.js");
            if (!File.Exists(scriptPath))
            {
                throw new InvalidOperationException($"Expected the real script at '{scriptPath}'.");
            }

            return File.ReadAllText(scriptPath);
        }

        private async Task<IPage> NewPageWithScriptLoadedAsync()
        {
            var page = await _fixture.NewPageAsync();
            await page.SetContentAsync("<!doctype html><html><body></body></html>");
            await page.EvaluateAsync(ScriptSource);
            return page;
        }

        // Calls _describeError with a JSON-shaped source object, returning the {friendly, raw}
        // result deserialized. The source is JSON round-tripped into the page since a plain
        // data shape (no real Error, no non-enumerable properties) is exactly what a resolved
        // {data, state:"failed"} pair's `data` half looks like in practice.
        private static async Task<(string? Friendly, string? Raw)> DescribeAsync(IPage page, object? source)
        {
            var inputJson = source is null ? "null" : JsonSerializer.Serialize(source);
            var resultJson = await page.EvaluateAsync<string>(
                "(input) => JSON.stringify(window.paypalCardFields._describeError(input === null ? null : JSON.parse(input)))",
                inputJson);
            using var doc = JsonDocument.Parse(resultJson);
            var friendly = doc.RootElement.TryGetProperty("friendly", out var f) && f.ValueKind != JsonValueKind.Null ? f.GetString() : null;
            var raw = doc.RootElement.TryGetProperty("raw", out var r) && r.ValueKind != JsonValueKind.Null ? r.GetString() : null;
            return (friendly, raw);
        }

        [Fact]
        public async Task NullSource_ReturnsNullFriendlyAndNullRaw()
        {
            var page = await NewPageWithScriptLoadedAsync();

            var (friendly, raw) = await DescribeAsync(page, null);

            Assert.Null(friendly);
            Assert.Null(raw);
        }

        [Fact]
        public async Task RealCapturedShape_SingleDetailDescription_ExtractsTheDescription()
        {
            // The real shape behind "Credit card number is not an accepted test number." -
            // details is a JSON-*encoded string*, not a nested object, per the real GraphQL
            // response captured live via the user's own browser DevTools this session.
            var page = await NewPageWithScriptLoadedAsync();
            var source = new
            {
                name = "UNPROCESSABLE_ENTITY",
                message = "The requested action could not be performed.",
                details = JsonSerializer.Serialize(new[]
                {
                    new { field = "/payment_source/card/number", issue = "CREDIT_CARD_NUMBER_MUST_BE_TEST_NUMBER", description = "Credit card number is not an accepted test number." }
                })
            };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal("Credit card number is not an accepted test number.", friendly);
        }

        [Fact]
        public async Task RealCapturedShape_TwoDetailDescriptions_JoinsBothWithASpace()
        {
            // The real two-error shape from the return_url/cancel_url Razor-@-prefix bug -
            // both errors' descriptions must show up, not just the first.
            var page = await NewPageWithScriptLoadedAsync();
            var source = new
            {
                message = "Invalid request - see details.",
                details = JsonSerializer.Serialize(new[]
                {
                    new { field = "/application_context/return_url", issue = "INVALID_PARAMETER_SYNTAX", description = "the value of a field does not conform to the expected format." },
                    new { field = "/application_context/cancel_url", issue = "INVALID_PARAMETER_SYNTAX", description = "the value of a field does not conform to the expected format." }
                })
            };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal(
                "the value of a field does not conform to the expected format. the value of a field does not conform to the expected format.",
                friendly);
        }

        [Fact]
        public async Task NestedDataDetails_ResolvedFailedSubmitShape_ExtractsTheDescription()
        {
            // submit()'s resolved-but-declined path hands describeError the `data` half of
            // {data, state:"failed"} directly - real shape is errors[0].data.details, so
            // describeError must also check source.data.details, not just source.details.
            var page = await NewPageWithScriptLoadedAsync();
            var source = new
            {
                data = new
                {
                    message = "Card declined.",
                    details = JsonSerializer.Serialize(new[]
                    {
                        new { issue = "INSTRUMENT_DECLINED", description = "The instrument presented either failed authentication or is invalid." }
                    })
                }
            };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal("The instrument presented either failed authentication or is invalid.", friendly);
        }

        [Fact]
        public async Task PlainMessageNoDetails_PassesThroughUnchanged()
        {
            var page = await NewPageWithScriptLoadedAsync();
            var source = new { message = "Failed to load the PayPal SDK script." };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal("Failed to load the PayPal SDK script.", friendly);
        }

        [Fact]
        public async Task NestedDataMessageNoDetails_FallsBackToDataMessage()
        {
            var page = await NewPageWithScriptLoadedAsync();
            var source = new { data = new { message = "Something went wrong." } };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal("Something went wrong.", friendly);
        }

        [Fact]
        public async Task PathologicalJsonAsMessage_TreatedAsNoUsableMessage()
        {
            // The real, live-captured pathological shape: some PayPal SDK error paths set
            // .message to a JSON-encoded dump of the error itself, with no .details to fall
            // back to - confirmed for real via this app's own Logs table during this session's
            // production debugging. Showing this raw JSON to a customer would defeat the whole
            // point of describeError existing; it must be treated as no usable message.
            var page = await NewPageWithScriptLoadedAsync();
            var source = new
            {
                name = "DevError",
                code = "ERR_DEV_RECEIVED_GRAPHQL_ERROR",
                message = """{"name":"DevError","code":"ERR_DEV_RECEIVED_GRAPHQL_ERROR","isRecoverable":false}"""
            };

            var (friendly, raw) = await DescribeAsync(page, source);

            Assert.Null(friendly);
            // The full raw dump must still be captured for server-side logging even though
            // nothing usable was found for the customer-facing message.
            Assert.Contains("ERR_DEV_RECEIVED_GRAPHQL_ERROR", raw);
        }

        [Fact]
        public async Task MalformedDetailsString_NotValidJson_FallsThroughToMessageWithoutThrowing()
        {
            var page = await NewPageWithScriptLoadedAsync();
            var source = new { message = "Fallback message.", details = "not-actually-json{{{" };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal("Fallback message.", friendly);
        }

        [Fact]
        public async Task EmptyDetailsArray_FallsThroughToMessage()
        {
            var page = await NewPageWithScriptLoadedAsync();
            var source = new { message = "Fallback message.", details = JsonSerializer.Serialize(Array.Empty<object>()) };

            var (friendly, _) = await DescribeAsync(page, source);

            Assert.Equal("Fallback message.", friendly);
        }

        [Fact]
        public async Task RealErrorObject_RawCaptureIncludesMessageAndStackViaGetOwnPropertyNames()
        {
            // Confirms raw capture uses Object.getOwnPropertyNames (not plain JSON.stringify,
            // which drops a real Error's non-enumerable message/stack) - constructed as a real
            // JS Error in-page, not JSON, since JSON can't represent non-enumerable properties.
            var page = await NewPageWithScriptLoadedAsync();

            var resultJson = await page.EvaluateAsync<string>(
                "() => { const e = new Error('boom-describe-error-test'); return JSON.stringify(window.paypalCardFields._describeError(e)); }");
            using var doc = JsonDocument.Parse(resultJson);
            var raw = doc.RootElement.GetProperty("raw").GetString();

            Assert.Contains("boom-describe-error-test", raw);
        }
    }
}
