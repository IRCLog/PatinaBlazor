using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers IStorageService.CancelOwnRentalAsync - the customer-facing equivalent of the
    // admin-only EndRentalAsync, built specifically because that admin method has no
    // ownership check at all (safe only because it's admin-gated). This is reachable
    // directly from /storage/account, so a wrong/guessed rentalId must never silently
    // succeed against another customer's rental.
    [Collection("Database")]
    public class CancelOwnRentalAsyncTests
    {
        private readonly DatabaseFixture _fixture;

        public CancelOwnRentalAsyncTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task CancelOwnRentalAsync_OwnerCancelsActiveRental_EndsRentalAndFreesUnit()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var (unit, customerId, adminId) = await CreatePropertyUnitAndCustomerAsync(storageService, userManager);

            var startDate = new DateTime(2026, 3, 15);
            var rental = await storageService.StartRentalAsync(unit.Id, customerId, unit.MonthlyRate,
                startDate, BillingFrequency.Monthly, startDate, adminId);

            var moveOutDate = startDate.AddMonths(2);
            var result = await storageService.CancelOwnRentalAsync(rental.Id, moveOutDate, customerId);

            Assert.True(result.Succeeded);
            Assert.Null(result.Error);

            var reloadedRental = await storageService.GetRentalByIdAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.Ended, reloadedRental!.Status);
            Assert.Equal(moveOutDate, reloadedRental.EndDate);

            var reloadedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Available, reloadedUnit!.Status);
        }

        [Fact]
        public async Task CancelOwnRentalAsync_DifferentCustomerAttemptsToCancel_IsRejectedAndRentalUntouched()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var (unit, customerId, adminId) = await CreatePropertyUnitAndCustomerAsync(storageService, userManager);

            var startDate = new DateTime(2026, 3, 15);
            var rental = await storageService.StartRentalAsync(unit.Id, customerId, unit.MonthlyRate,
                startDate, BillingFrequency.Monthly, startDate, adminId);

            var impostorEmail = $"cancelimpostor_{Guid.NewGuid():N}@patinablazor.local";
            var impostor = new ApplicationUser
            {
                UserName = impostorEmail,
                Email = impostorEmail,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var createResult = await userManager.CreateAsync(impostor, "ThrowawayTest123!");
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));

            var result = await storageService.CancelOwnRentalAsync(rental.Id, startDate.AddMonths(1), impostor.Id);

            Assert.False(result.Succeeded);
            Assert.Equal("This rental does not belong to you.", result.Error);

            var reloadedRental = await storageService.GetRentalByIdAsync(rental.Id);
            Assert.Equal(StorageRentalStatus.Active, reloadedRental!.Status);

            var reloadedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Occupied, reloadedUnit!.Status);
        }

        [Fact]
        public async Task CancelOwnRentalAsync_UnknownRentalId_ReturnsFriendlyFailureRatherThanThrowing()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

            var result = await storageService.CancelOwnRentalAsync(rentalId: -1, DateTime.Today, customerUserId: "irrelevant");

            Assert.False(result.Succeeded);
            Assert.Equal("Rental not found.", result.Error);
        }

        [Fact]
        public async Task CancelOwnRentalAsync_RentalAlreadyEnded_IsRejected()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var (unit, customerId, adminId) = await CreatePropertyUnitAndCustomerAsync(storageService, userManager);

            var startDate = new DateTime(2026, 3, 15);
            var rental = await storageService.StartRentalAsync(unit.Id, customerId, unit.MonthlyRate,
                startDate, BillingFrequency.Monthly, startDate, adminId);

            await storageService.EndRentalAsync(rental.Id, startDate.AddMonths(1), adminId);

            var result = await storageService.CancelOwnRentalAsync(rental.Id, startDate.AddMonths(2), customerId);

            Assert.False(result.Succeeded);
            Assert.Equal("This rental has already ended.", result.Error);
        }

        private static async Task<(StorageUnit Unit, string CustomerId, string AdminId)> CreatePropertyUnitAndCustomerAsync(
            IStorageService storageService, UserManager<ApplicationUser> userManager)
        {
            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            Assert.NotNull(admin);

            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = $"Cancel Rental Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Test Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);

            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "CANCEL-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = 120m
            }, admin.Id);

            var customerEmail = $"cancelrentaltest_{Guid.NewGuid():N}@patinablazor.local";
            var customer = new ApplicationUser
            {
                UserName = customerEmail,
                Email = customerEmail,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow
            };
            var createResult = await userManager.CreateAsync(customer, "ThrowawayTest123!");
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));

            return (unit, customer.Id, admin.Id);
        }
    }
}
