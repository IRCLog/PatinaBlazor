using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace PatinaBlazor.Services
{
    public class RecaptchaService : IRecaptchaService
    {
        private const string VerifyUrl = "https://www.google.com/recaptcha/api/siteverify";

        private readonly HttpClient _httpClient;
        private readonly RecaptchaOptions _options;
        private readonly ILogger<RecaptchaService> _logger;

        public RecaptchaService(HttpClient httpClient, IOptions<RecaptchaOptions> options, ILogger<RecaptchaService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public string SiteKey => _options.SiteKey;

        public async Task<RecaptchaVerificationResult> VerifyAsync(string token, string expectedAction, CancellationToken cancellationToken = default)
        {
            // Deliberately does not block real users while reCAPTCHA is still being set up
            // (e.g. before real keys exist in dotnet user-secrets) - the same "fail open when
            // not yet configured" reasoning used for other optional integrations in this app.
            if (string.IsNullOrEmpty(_options.SecretKey))
            {
                _logger.LogWarning("reCAPTCHA verification skipped for action {Action} - Recaptcha:SecretKey is not configured.", expectedAction);
                return new RecaptchaVerificationResult { Succeeded = true };
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogSecurityWarning("reCAPTCHA verification failed for action {Action}: no token was provided (client-side execute() likely failed or was skipped).", expectedAction);
                return new RecaptchaVerificationResult { Succeeded = false, Error = "We couldn't verify you're not a robot. Please try again." };
            }

            try
            {
                using var response = await _httpClient.PostAsync(VerifyUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = _options.SecretKey,
                    ["response"] = token
                }), cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var json = JsonNode.Parse(body);

                var success = json?["success"]?.GetValue<bool>() ?? false;
                if (!success)
                {
                    var errorCodes = json?["error-codes"]?.AsArray().Select(e => e?.GetValue<string>()).ToList();
                    _logger.LogSecurityWarning("reCAPTCHA verification failed for action {Action}: {ErrorCodes}", expectedAction, errorCodes is { Count: > 0 } ? string.Join(", ", errorCodes) : "(none)");
                    return new RecaptchaVerificationResult { Succeeded = false, Error = "We couldn't verify you're not a robot. Please try again." };
                }

                // action/score are v3-only fields - absent for a v2 (checkbox) token, e.g.
                // Google's own published "always passes" v2 test key pair (used in this
                // project's live smoke test - see RecaptchaServiceTests.cs), which never
                // populates either. A null action/score is treated as nothing to check,
                // rather than a failure.
                var action = json?["action"]?.GetValue<string>();
                if (action is not null && !string.Equals(action, expectedAction, StringComparison.Ordinal))
                {
                    _logger.LogSecurityWarning("reCAPTCHA action mismatch: expected {Expected}, got {Actual}.", expectedAction, action);
                    return new RecaptchaVerificationResult { Succeeded = false, Error = "We couldn't verify you're not a robot. Please try again." };
                }

                var score = json?["score"]?.GetValue<double?>();
                if (score is not null && score < _options.MinimumScore)
                {
                    _logger.LogSecurityWarning("reCAPTCHA score {Score} was below the minimum {MinimumScore} for action {Action}.", score, _options.MinimumScore, expectedAction);
                    return new RecaptchaVerificationResult { Succeeded = false, Error = "We couldn't verify you're not a robot. Please try again." };
                }

                return new RecaptchaVerificationResult { Succeeded = true };
            }
            catch (Exception ex)
            {
                // Fails open, not closed - a transient Google outage or network blip
                // shouldn't be able to take down this app's own signup flows entirely.
                // reCAPTCHA is advisory anti-abuse protection, not a hard security boundary
                // the way, say, a payment call is - losing the check for one request is a far
                // smaller risk than breaking signup for every real customer during an outage.
                _logger.LogError(ex, "reCAPTCHA verification call failed for action {Action} - failing open.", expectedAction);
                return new RecaptchaVerificationResult { Succeeded = true };
            }
        }
    }
}
