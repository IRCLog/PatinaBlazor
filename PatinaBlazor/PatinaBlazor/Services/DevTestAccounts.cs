namespace PatinaBlazor.Services
{
    // Well-known credentials for a seeded, non-production account used by both interactive
    // dev-time testing and the automated test suite (PatinaBlazor.Tests) - one seeding path
    // (DatabaseSeeder.EnsureTestAutomationAccountAsync) creates it, gated to run only outside
    // Production, so both the real shared dev DB (via normal app startup) and a throwaway
    // Testcontainers DB (via the test project's fixture calling the same seeder) end up with
    // the same account under the same credentials.
    public static class DevTestAccounts
    {
        public const string AutomationEmail = "test.automation@patinablazor.local";
        public const string AutomationPassword = "AutomationTest123!";
    }
}
