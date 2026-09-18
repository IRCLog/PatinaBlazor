using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.E2ETests
{
    // Covers /storage/account (the wallet page, Part 3 of the 2026-09 Phase 2b checkpoint
    // entry) - access control plus rendering real data seeded directly against the DB (the
    // same direct-SQL/EF-seeding technique AdminAccessTests uses for its "RendersRealData"
    // test), since driving the full signup -> PayPal Vault -> charge flow through a real
    // browser isn't something CI can do (see StorageSignUpPaymentTests' own comment on why).
    [Collection("E2E")]
    public class StorageAccountTests
    {
        private readonly WebAppFixture _fixture;

        public StorageAccountTests(WebAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StorageAccount_AnonymousUser_RedirectsToLogin()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/storage/account");

            Assert.Contains("/Account/Login", page.Url);
        }

        [Fact]
        public async Task StorageAccount_LoggedInAsNonStorageCustomer_IsForbidden()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.AutomationEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/storage/account");

            var heading = page.GetByText("My Storage Account");
            await Task.Delay(1000);
            Assert.Equal(0, await heading.CountAsync());
        }

        [Fact]
        public async Task StorageAccount_LoggedInWithNoRental_ShowsSignUpPrompt()
        {
            await using var context = await _fixture.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, DevTestAccounts.StorageCustomerEmail, DevTestAccounts.Password);
            await page.GotoAsync($"{_fixture.BaseUrl}/storage/account");

            await page.GetByText("You don't have a storage rental on file yet").WaitForAsync(new() { Timeout = 10_000 });
        }

        [Fact]
        public async Task StorageAccount_RealRentalAndBillingHistorySeeded_RendersThemCorrectly()
        {
            // A throwaway account created through the real /storage/signup form + a real
            // Mailpit-delivered confirmation link, not the shared DevTestAccounts.
            // StorageCustomerEmail account - that account is also used by the
            // "no rental yet" test above, and a rental seeded here would otherwise leak into
            // that test depending on execution order (confirmed this really happens: an
            // earlier version of this file shared the one dev account and the "no rental"
            // test failed whenever it ran after this one).
            var email = $"e2e-wallettest-{Guid.NewGuid():N}@example.com";
            const string password = "E2eTest123!";
            await using var setupContext = await _fixture.NewContextAsync();
            var setupPage = await setupContext.NewPageAsync();
            await CreateConfirmedStorageCustomerAsync(setupContext, setupPage, email, password);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_fixture.DbConnectionString).Options;
            int rentalId;
            await using (var dbContext = new ApplicationDbContext(options))
            {
                var customer = await dbContext.Users.SingleAsync(u => u.Email == email);
                var admin = await dbContext.Users.SingleAsync(u => u.Email == DevTestAccounts.AdminEmail);

                var property = new StorageProperty
                {
                    Name = $"Wallet Test Property {Guid.NewGuid():N}",
                    AddressLine1 = "1 Wallet Way",
                    City = "Testville",
                    State = "CA",
                    PostalCode = "90000",
                    CreatedByUserId = admin.Id,
                    ModifiedByUserId = admin.Id,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                dbContext.StorageProperties.Add(property);
                await dbContext.SaveChangesAsync();

                var unit = new StorageUnit
                {
                    StoragePropertyId = property.Id,
                    UnitNumber = "W-42",
                    LengthFeet = 10,
                    WidthFeet = 15,
                    HeightFeet = 8,
                    MonthlyRate = 175m,
                    Status = StorageUnitStatus.Occupied,
                    CreatedByUserId = admin.Id,
                    ModifiedByUserId = admin.Id,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                dbContext.StorageUnits.Add(unit);
                await dbContext.SaveChangesAsync();

                var now = DateTime.UtcNow;
                var rental = new StorageRental
                {
                    StorageUnitId = unit.Id,
                    CustomerUserId = customer.Id,
                    StartDate = now.AddMonths(-2),
                    PaymentDate = now.AddMonths(-2),
                    LastBilledDate = now.AddDays(-3),
                    MonthlyRateAtSigning = 175m,
                    BillingFrequency = BillingFrequency.Monthly,
                    Status = StorageRentalStatus.Active,
                    CreatedByUserId = customer.Id,
                    ModifiedByUserId = customer.Id,
                    CreatedDate = now.AddMonths(-2),
                    ModifiedDate = now
                };
                dbContext.StorageRentals.Add(rental);

                dbContext.StoragePaymentMethods.Add(new StoragePaymentMethod
                {
                    UserId = customer.Id,
                    PayPalPaymentTokenId = $"VAULT-{Guid.NewGuid():N}",
                    DisplayLabel = "wallettest@example.com",
                    CreatedDate = now
                });

                await dbContext.SaveChangesAsync();
                rentalId = rental.Id;

                dbContext.StoragePaymentTransactions.Add(new StoragePaymentTransaction
                {
                    StorageRentalId = rentalId,
                    PayPalOrderId = "REAL-LOOKING-ORDER-1",
                    Amount = 175m,
                    Succeeded = true,
                    OccurredAtUtc = now.AddDays(-3)
                });
                await dbContext.SaveChangesAsync();
            }

            await using var browserContext = await _fixture.NewContextAsync();
            var page = await browserContext.NewPageAsync();

            await page.LoginAsync(_fixture.BaseUrl, email, password);
            await page.GotoAsync($"{_fixture.BaseUrl}/storage/account");

            await page.GetByText("Unit W-42", new() { Exact = false }).WaitForAsync(new() { Timeout = 10_000 });
            var bodyText = await page.InnerTextAsync("body");

            Assert.Contains("wallettest@example.com", bodyText);
            Assert.Contains("175.00", bodyText);
            Assert.Contains("REAL-LOOKING-ORDER-1", bodyText);
        }

        // Registers and confirms a real, throwaway Storage Customer account via the real
        // /storage/signup wizard's first two steps (account info + live email verification),
        // exactly like StorageSignUpPaymentTests - the registration service assigns the
        // Storage Customer role automatically, so this account satisfies
        // /storage/account's [Authorize(Roles = "Storage Customer")] without any manual
        // role-assignment SQL. Deliberately stops there (never reserves a unit or touches
        // PayPal) - this test seeds its own rental data directly afterward.
        private async Task CreateConfirmedStorageCustomerAsync(IBrowserContext context, IPage wizardPage, string email, string password)
        {
            await wizardPage.GotoAsync($"{_fixture.BaseUrl}/storage/signup");
            await wizardPage.GetByPlaceholder("name@example.com").FillAsync(email);
            await wizardPage.GetByPlaceholder("First name").FillAsync("Wally");
            await wizardPage.GetByPlaceholder("Last name").FillAsync($"E2E{Guid.NewGuid():N}"[..10]);
            await wizardPage.GetByPlaceholder("password", new() { Exact = true }).FillAsync(password);
            await wizardPage.GetByPlaceholder("confirm password").FillAsync(password);
            await wizardPage.GetByPlaceholder("Phone number").FillAsync("555-0123");
            await wizardPage.GetByPlaceholder("Address line 1").FillAsync("1 Wallet Test Ave");
            await wizardPage.GetByPlaceholder("City").FillAsync("Testville");
            await wizardPage.GetByPlaceholder("State").FillAsync("CA");
            await wizardPage.GetByPlaceholder("Postal code").FillAsync("90001");
            await wizardPage.GetByPlaceholder("Emergency contact name").FillAsync("Sam Backup");
            await wizardPage.GetByPlaceholder("Emergency contact phone").FillAsync("555-0199");
            await wizardPage.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

            await wizardPage.GetByText("Check your email").WaitForAsync(new() { Timeout = 10_000 });

            var confirmationLink = await _fixture.Mailpit.GetLatestEmailLinkAsync(email, "ConfirmEmail", TimeSpan.FromSeconds(20));
            var confirmPage = await context.NewPageAsync();
            await confirmPage.GotoAsync(confirmationLink);
            await confirmPage.GetByText("Thank you for confirming your email").WaitForAsync(new() { Timeout = 10_000 });
            await confirmPage.CloseAsync();
        }
    }
}
