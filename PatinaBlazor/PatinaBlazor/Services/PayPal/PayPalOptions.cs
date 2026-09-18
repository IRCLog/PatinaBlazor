namespace PatinaBlazor.Services.PayPal
{
    // Bound from the "Paypal" configuration section. ClientId/ClientSecret come from
    // dotnet user-secrets locally (never committed - see the 2026-09-16 checkpoint entry
    // on how the real sandbox credentials were found sitting in a tracked appsettings file
    // and moved out). BaseUrl defaults to the sandbox API host; going live later is just
    // swapping BaseUrl to https://api-m.paypal.com plus live credentials, no code changes.
    public class PayPalOptions
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";
        public string BrandName { get; set; } = "PatinaBlazor";
    }
}
