using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PatinaBlazor.Services;
using PatinaBlazor.Tests.Fakes;

namespace PatinaBlazor.Tests
{
    // Covers RecaptchaService's handling of Google's real documented reCAPTCHA v3
    // siteverify response shapes - fully offline via FakeHttpMessageHandler, same pattern as
    // PayPalClientTests.cs, plus one permanent LIVE call against Google's own publicly
    // documented "always passes" test key pair (see the last test below) - unlike PayPal's
    // sandbox, this needs no project-specific secrets and is safe to run in CI.
    public class RecaptchaServiceTests
    {
        private static RecaptchaService CreateService(FakeHttpMessageHandler handler, double minimumScore = 0.5, string secretKey = "test-secret")
        {
            var httpClient = new HttpClient(handler);
            var options = Options.Create(new RecaptchaOptions { SiteKey = "test-site-key", SecretKey = secretKey, MinimumScore = minimumScore });
            return new RecaptchaService(httpClient, options, NullLogger<RecaptchaService>.Instance);
        }

        [Fact]
        public async Task VerifyAsync_SuccessWithHighScoreAndMatchingAction_ReturnsSucceeded()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/recaptcha/api/siteverify", HttpStatusCode.OK, """
                {"success":true,"score":0.9,"action":"register","challenge_ts":"2026-09-19T00:00:00Z","hostname":"example.com"}
                """);
            var service = CreateService(handler);

            var result = await service.VerifyAsync("real-token", "register");

            Assert.True(result.Succeeded, result.Error);
        }

        [Fact]
        public async Task VerifyAsync_SuccessButScoreBelowMinimum_ReturnsFailed()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/recaptcha/api/siteverify", HttpStatusCode.OK, """
                {"success":true,"score":0.1,"action":"register","challenge_ts":"2026-09-19T00:00:00Z","hostname":"example.com"}
                """);
            var service = CreateService(handler, minimumScore: 0.5);

            var result = await service.VerifyAsync("real-token", "register");

            Assert.False(result.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }

        [Fact]
        public async Task VerifyAsync_ScoreExactlyAtMinimum_IsAccepted()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/recaptcha/api/siteverify", HttpStatusCode.OK, """
                {"success":true,"score":0.5,"action":"register","challenge_ts":"2026-09-19T00:00:00Z","hostname":"example.com"}
                """);
            var service = CreateService(handler, minimumScore: 0.5);

            var result = await service.VerifyAsync("real-token", "register");

            Assert.True(result.Succeeded, result.Error);
        }

        [Fact]
        public async Task VerifyAsync_ActionMismatch_ReturnsFailed()
        {
            // A real, meaningful signal - a token generated for one form (or a stale/reused
            // token) presented to a different action's verification call.
            var handler = new FakeHttpMessageHandler();
            handler.On("/recaptcha/api/siteverify", HttpStatusCode.OK, """
                {"success":true,"score":0.9,"action":"login","challenge_ts":"2026-09-19T00:00:00Z","hostname":"example.com"}
                """);
            var service = CreateService(handler);

            var result = await service.VerifyAsync("real-token", "register");

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task VerifyAsync_SuccessFalse_ReturnsFailedWithoutThrowing()
        {
            var handler = new FakeHttpMessageHandler();
            handler.On("/recaptcha/api/siteverify", HttpStatusCode.OK, """
                {"success":false,"error-codes":["invalid-input-response"]}
                """);
            var service = CreateService(handler);

            var result = await service.VerifyAsync("bad-token", "register");

            Assert.False(result.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }

        [Fact]
        public async Task VerifyAsync_V2StyleResponseWithNoScoreOrAction_TreatedAsSucceeded()
        {
            // The real shape Google's documented v2 "always passes" test key pair returns
            // (confirmed live - see the smoke test below) - no score, no action field at all.
            // Nothing to check against expectedAction/MinimumScore in that case, so this must
            // not be treated as a failure.
            var handler = new FakeHttpMessageHandler();
            handler.On("/recaptcha/api/siteverify", HttpStatusCode.OK, """
                {"success":true,"challenge_ts":"2026-09-19T04:17:26Z","hostname":"testkey.google.com"}
                """);
            var service = CreateService(handler);

            var result = await service.VerifyAsync("any-token", "register");

            Assert.True(result.Succeeded, result.Error);
        }

        [Fact]
        public async Task VerifyAsync_EmptyToken_ReturnsFailedWithoutCallingTheNetwork()
        {
            var handler = new FakeHttpMessageHandler();
            var service = CreateService(handler);

            var result = await service.VerifyAsync("", "register");

            Assert.False(result.Succeeded);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task VerifyAsync_SecretKeyNotConfigured_SkipsVerificationAndSucceedsWithoutCallingTheNetwork()
        {
            var handler = new FakeHttpMessageHandler();
            var service = CreateService(handler, secretKey: "");

            var result = await service.VerifyAsync("some-token", "register");

            Assert.True(result.Succeeded, result.Error);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task VerifyAsync_NetworkExceptionMidRequest_FailsOpenReturnsSucceeded()
        {
            // Deliberate fail-open: reCAPTCHA is advisory anti-abuse protection, not a hard
            // security boundary the way a payment call is - a Google outage shouldn't be able
            // to take down this app's own signup flows.
            var handler = new FakeHttpMessageHandler();
            handler.Throw("/recaptcha/api/siteverify", new HttpRequestException("Connection reset"));
            var service = CreateService(handler);

            var result = await service.VerifyAsync("real-token", "register");

            Assert.True(result.Succeeded, result.Error);
        }

        [Fact]
        public async Task VerifyAsync_RealGoogleTestKeyPair_LiveCallSucceeds()
        {
            // A REAL, live network call - not a fixture - to Google's real siteverify API,
            // using Google's own officially documented "always passes" reCAPTCHA test key
            // pair (site key 6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI / secret key
            // 6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe - published across many real projects'
            // setup docs, and confirmed directly against the real endpoint before writing this
            // test: it returns {"success":true,"challenge_ts":...,"hostname":
            // "testkey.google.com"} regardless of the token value). Unlike the PayPal sandbox
            // dev-only suite, this needs no project-specific secrets and is safe to run
            // unconditionally in CI - it's Google's own published test infrastructure, not
            // this app's real production credentials.
            var httpClient = new HttpClient();
            var options = Options.Create(new RecaptchaOptions
            {
                SiteKey = "6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI",
                SecretKey = "6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe",
                MinimumScore = 0.5
            });
            var service = new RecaptchaService(httpClient, options, NullLogger<RecaptchaService>.Instance);

            var result = await service.VerifyAsync("any-token-value-works-with-this-test-key", "register");

            Assert.True(result.Succeeded, result.Error);
        }
    }
}
