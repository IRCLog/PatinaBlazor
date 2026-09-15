using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers StorageCustomerRegistrationService - the real logic behind the /storage/signup
    // page, pulled into a service specifically so it's testable without a browser (same
    // reasoning as AdminDashboardService's extraction). Scenarios worth covering: a normal
    // successful signup persists everything correctly (user, phone, profile, role); the
    // DisplayName-collision auto-disambiguation (a real, deliberate UX decision - see
    // CLAUDE.md - two customers with the same real name must both succeed, not have the
    // second rejected); a duplicate email is still rejected cleanly.
    [Collection("Database")]
    public class StorageCustomerRegistrationServiceTests
    {
        private readonly DatabaseFixture _fixture;

        public StorageCustomerRegistrationServiceTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        private static StorageCustomerRegistrationRequest MakeRequest(string email, string firstName, string lastName) => new(
            Email: email,
            Password: "ThrowawayTest123!",
            FirstName: firstName,
            LastName: lastName,
            PhoneNumber: "555-0100",
            AddressLine1: "123 Main St",
            AddressLine2: "Unit 4",
            City: "Bakersfield",
            State: "CA",
            PostalCode: "93301",
            EmergencyContactName: "Jane Doe",
            EmergencyContactPhone: "555-0199");

        [Fact]
        public async Task RegisterAsync_ValidRequest_PersistsUserPhoneProfileAndRole()
        {
            using var scope = _fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IStorageCustomerRegistrationService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var email = $"regtest_{Guid.NewGuid():N}@patinablazor.local";
            var result = await service.RegisterAsync(MakeRequest(email, "Robert", "Rentaltest"));

            Assert.True(result.Succeeded, string.Join(", ", result.Errors));
            Assert.NotNull(result.User);
            Assert.Equal("Robert Rentaltest", result.User!.DisplayName);

            var reloaded = await userManager.FindByEmailAsync(email);
            Assert.NotNull(reloaded);
            Assert.Equal("555-0100", await userManager.GetPhoneNumberAsync(reloaded!));
            Assert.True(await userManager.IsInRoleAsync(reloaded!, StorageService.StorageCustomerRoleName));

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var profile = await context.StorageCustomerProfiles.FindAsync(reloaded!.Id);
            Assert.NotNull(profile);
            Assert.Equal("123 Main St", profile!.AddressLine1);
            Assert.Equal("Unit 4", profile.AddressLine2);
            Assert.Equal("Bakersfield", profile.City);
            Assert.Equal("CA", profile.State);
            Assert.Equal("93301", profile.PostalCode);
            Assert.Equal("Jane Doe", profile.EmergencyContactName);
            Assert.Equal("555-0199", profile.EmergencyContactPhone);
        }

        [Fact]
        public async Task RegisterAsync_TwoCustomersWithSameName_AutoDisambiguatesInsteadOfRejecting()
        {
            using var scope = _fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IStorageCustomerRegistrationService>();

            var sharedFirstName = $"Samename{Guid.NewGuid():N}"[..20];
            var email1 = $"regtest_{Guid.NewGuid():N}@patinablazor.local";
            var email2 = $"regtest_{Guid.NewGuid():N}@patinablazor.local";

            var first = await service.RegisterAsync(MakeRequest(email1, sharedFirstName, "Smith"));
            var second = await service.RegisterAsync(MakeRequest(email2, sharedFirstName, "Smith"));

            Assert.True(first.Succeeded, string.Join(", ", first.Errors));
            Assert.True(second.Succeeded, string.Join(", ", second.Errors));
            Assert.Equal($"{sharedFirstName} Smith", first.User!.DisplayName);
            Assert.Equal($"{sharedFirstName} Smith 2", second.User!.DisplayName);
        }

        [Fact]
        public async Task RegisterAsync_DuplicateEmail_FailsCleanlyWithoutThrowing()
        {
            using var scope = _fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IStorageCustomerRegistrationService>();

            var email = $"regtest_{Guid.NewGuid():N}@patinablazor.local";
            var first = await service.RegisterAsync(MakeRequest(email, "First", "Attempt"));
            Assert.True(first.Succeeded);

            var second = await service.RegisterAsync(MakeRequest(email, "Second", "Attempt"));

            Assert.False(second.Succeeded);
            Assert.Null(second.User);
            Assert.NotEmpty(second.Errors);
        }
    }
}
