using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers StoragePaymentService.ReserveUnitAsync - the self-service unit-claiming step
    // of the storage signup wizard (Phase 1 of the 2026-09 Phase 2b checkpoint entry).
    [Collection("Database")]
    public class StoragePaymentServiceTests
    {
        private readonly DatabaseFixture _fixture;

        public StoragePaymentServiceTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ReserveUnitAsync_AvailableUnit_CreatesPendingPaymentRentalAndReservesUnit()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var customerId = await CreateThrowawayCustomerAsync(userManager);
            var unit = await CreateAvailableUnitAsync(storageService, admin!.Id, rate: 150m);

            var result = await paymentService.ReserveUnitAsync(unit.Id, customerId);

            Assert.True(result.Succeeded, result.Error);
            Assert.NotNull(result.Rental);
            Assert.Equal(StorageRentalStatus.PendingPayment, result.Rental!.Status);
            Assert.Equal(150m, result.Rental.MonthlyRateAtSigning);
            Assert.Equal(customerId, result.Rental.CustomerUserId);

            var reloadedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Reserved, reloadedUnit!.Status);
        }

        [Fact]
        public async Task ReserveUnitAsync_UnitAlreadyReserved_FailsWithoutCreatingARental()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var firstCustomerId = await CreateThrowawayCustomerAsync(userManager);
            var secondCustomerId = await CreateThrowawayCustomerAsync(userManager);
            var unit = await CreateAvailableUnitAsync(storageService, admin!.Id, rate: 100m);

            var first = await paymentService.ReserveUnitAsync(unit.Id, firstCustomerId);
            Assert.True(first.Succeeded);

            var second = await paymentService.ReserveUnitAsync(unit.Id, secondCustomerId);

            Assert.False(second.Succeeded);
            Assert.Null(second.Rental);
            Assert.False(string.IsNullOrEmpty(second.Error));
        }

        [Fact]
        public async Task ReserveUnitAsync_CustomerPicksADifferentUnit_ReleasesThePreviousReservation()
        {
            using var scope = _fixture.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var customerId = await CreateThrowawayCustomerAsync(userManager);
            var firstUnit = await CreateAvailableUnitAsync(storageService, admin!.Id, rate: 100m);
            var secondUnit = await CreateAvailableUnitAsync(storageService, admin.Id, rate: 120m);

            var first = await paymentService.ReserveUnitAsync(firstUnit.Id, customerId);
            Assert.True(first.Succeeded);

            var second = await paymentService.ReserveUnitAsync(secondUnit.Id, customerId);
            Assert.True(second.Succeeded);

            var reloadedFirstUnit = await storageService.GetUnitByIdAsync(firstUnit.Id);
            Assert.Equal(StorageUnitStatus.Available, reloadedFirstUnit!.Status);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
            var firstRental = await context.StorageRentals.FindAsync(first.Rental!.Id);
            Assert.Equal(StorageRentalStatus.Ended, firstRental!.Status);

            var reloadedSecondUnit = await storageService.GetUnitByIdAsync(secondUnit.Id);
            Assert.Equal(StorageUnitStatus.Reserved, reloadedSecondUnit!.Status);
        }

        // The real point of this test: proves the reservation is a genuine atomic
        // check-and-set, not a read-then-write race. Two customers hitting
        // ReserveUnitAsync for the exact same unit at the exact same moment must result
        // in exactly one success - not two, and not zero.
        [Fact]
        public async Task ReserveUnitAsync_ConcurrentRequestsForTheSameUnit_OnlyOneSucceeds()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            var customerAId = await CreateThrowawayCustomerAsync(userManager);
            var customerBId = await CreateThrowawayCustomerAsync(userManager);
            var unit = await CreateAvailableUnitAsync(storageService, admin!.Id, rate: 100m);

            // Separate IStoragePaymentService instances (their own DbContext per call
            // already, via IDbContextFactory) so this is a genuine concurrent race, not
            // two sequential awaits on the same instance.
            using var scopeA = _fixture.CreateScope();
            using var scopeB = _fixture.CreateScope();
            var paymentServiceA = scopeA.ServiceProvider.GetRequiredService<IStoragePaymentService>();
            var paymentServiceB = scopeB.ServiceProvider.GetRequiredService<IStoragePaymentService>();

            var taskA = paymentServiceA.ReserveUnitAsync(unit.Id, customerAId);
            var taskB = paymentServiceB.ReserveUnitAsync(unit.Id, customerBId);
            var results = await Task.WhenAll(taskA, taskB);

            Assert.Single(results, r => r.Succeeded);
            Assert.Single(results, r => !r.Succeeded);
        }

        private static async Task<string> CreateThrowawayCustomerAsync(UserManager<ApplicationUser> userManager)
        {
            var email = $"paytest_{Guid.NewGuid():N}@patinablazor.local";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "ThrowawayTest123!");
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
            return user.Id;
        }

        private static async Task<StorageUnit> CreateAvailableUnitAsync(IStorageService storageService, string adminId, decimal rate)
        {
            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = $"Payment Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Test Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, adminId);

            return await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "PAY-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = rate
            }, adminId);
        }
    }
}
