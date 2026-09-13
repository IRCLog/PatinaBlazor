namespace PatinaBlazor.Services
{
    // Well-known credentials for a small set of seeded, non-production accounts - one per
    // role plus one with no role at all - used by both interactive dev-time testing and the
    // automated test suite (PatinaBlazor.Tests). One seeding path
    // (DatabaseSeeder.EnsureDevTestAccountsAsync) creates all of them, gated to run only
    // outside Production, so both the real shared dev DB (via normal app startup) and a
    // throwaway Testcontainers DB (via the test project's fixture calling the same seeder)
    // end up with the same accounts under the same credentials. All share one password -
    // these are low-stakes, throwaway accounts that never exist in Production, so a separate
    // password per role buys nothing.
    public static class DevTestAccounts
    {
        public const string Password = "DevTest123!";

        // No role - a plain authenticated user, e.g. for testing behavior that only requires
        // being logged in.
        public const string AutomationEmail = "test.automation@patinablazor.local";

        // Admin - deliberately separate from the real seeded admin (adamsilzell@gmail.com),
        // so admin-only paths can be exercised in tests/dev without touching that real
        // account's credentials.
        public const string AdminEmail = "test.admin@patinablazor.local";

        public const string StorageAdminEmail = "test.storageadmin@patinablazor.local";
        public const string StorageCustomerEmail = "test.storagecustomer@patinablazor.local";
        public const string ArticlePublisherEmail = "test.articlepublisher@patinablazor.local";
    }
}
