namespace PatinaBlazor.Services
{
    // Bound from the "Recaptcha" configuration section. SiteKey/SecretKey come from
    // dotnet user-secrets locally (never committed), matching the established pattern for
    // Paypal:ClientId/ClientSecret - SiteKey is technically public (it's embedded in
    // client-side JS), but is kept alongside SecretKey for consistency with that existing
    // convention rather than splitting it out to appsettings.json.
    public class RecaptchaOptions
    {
        public string SiteKey { get; set; } = "";
        public string SecretKey { get; set; } = "";

        // reCAPTCHA v3 scores range 0.0 (very likely a bot) to 1.0 (very likely a real
        // human). Google's own guidance suggests 0.5 as a reasonable starting point,
        // adjustable per site based on observed real-traffic score distribution.
        public double MinimumScore { get; set; } = 0.5;
    }
}
