namespace PatinaBlazor.Services
{
    public class RecaptchaVerificationResult
    {
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
    }

    // Wraps Google reCAPTCHA v3 (score-based, no click-a-challenge UI - the customer never
    // sees anything, it just runs invisibly and scores how bot-like the request looks).
    public interface IRecaptchaService
    {
        // The public site key, embedded in client-side JS to load Google's script and call
        // grecaptcha.execute() - empty if reCAPTCHA hasn't been configured yet (see
        // VerifyAsync's own not-configured handling), in which case callers should skip
        // calling the client-side JS at all rather than trying to load a script with an
        // empty key.
        string SiteKey { get; }

        // Verifies a token produced by grecaptcha.execute(SiteKey, { action: expectedAction })
        // against Google's real siteverify API. expectedAction must match the action string
        // passed to execute() - Google echoes it back, and a mismatch is treated as a failure
        // (a legitimate signal the token was reused from a different, unrelated form).
        Task<RecaptchaVerificationResult> VerifyAsync(string token, string expectedAction, CancellationToken cancellationToken = default);
    }
}
