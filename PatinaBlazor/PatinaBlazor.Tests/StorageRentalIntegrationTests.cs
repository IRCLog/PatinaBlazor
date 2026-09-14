using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers StorageService.StartRentalAsync's PaymentDate validation (a real, explicit user
    // constraint - see CLAUDE.md's 2026-09-05 PaymentDate entry - previously verified once by
    // hand via a throwaway console harness plus a partial live-browser boundary check, both
    // since gone), the Occupied-status/role-assignment side effects of starting a rental, and
    // UpdateUnitAsync's guard against a direct edit setting Status = Occupied (which is
    // exclusively a side effect of Start/EndRentalAsync).
    [Collection("Database")]
    public class StorageRentalIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public StorageRentalIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StartRentalAsync_PaymentDateBeforeStartDate_ThrowsArgumentException()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

            var startDate = new DateTime(2026, 3, 15);
            var paymentDate = startDate.AddDays(-1);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                storageService.StartRentalAsync(unitId: 0, customerUserId: "irrelevant", monthlyRate: 100m,
                    startDate, BillingFrequency.Monthly, paymentDate, currentUserId: "irrelevant"));
        }

        [Fact]
        public async Task StartRentalAsync_PaymentDateMoreThanOneMonthAfterStartDate_ThrowsArgumentException()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

            var startDate = new DateTime(2026, 3, 15);
            var paymentDate = startDate.AddMonths(1).AddDays(1);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                storageService.StartRentalAsync(unitId: 0, customerUserId: "irrelevant", monthlyRate: 100m,
                    startDate, BillingFrequency.Monthly, paymentDate, currentUserId: "irrelevant"));
        }

        [Fact]
        public async Task StartRentalAsync_PaymentDateExactlyOneMonthAfterStartDate_IsAccepted()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var (unit, customerId, adminId) = await CreatePropertyUnitAndCustomerAsync(scope, storageService, userManager);

            var startDate = new DateTime(2026, 3, 15);
            var paymentDate = startDate.AddMonths(1); // exact boundary

            var rental = await storageService.StartRentalAsync(unit.Id, customerId, unit.MonthlyRate,
                startDate, BillingFrequency.Monthly, paymentDate, adminId);

            Assert.Equal(paymentDate, rental.PaymentDate);
        }

        [Fact]
        public async Task StartRentalAsync_MarksUnitOccupiedAndAssignsStorageCustomerRoleToPreviouslyUnroledCustomer()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var (unit, customerId, adminId) = await CreatePropertyUnitAndCustomerAsync(scope, storageService, userManager);

            var customer = await userManager.FindByIdAsync(customerId);
            Assert.False(await userManager.IsInRoleAsync(customer!, StorageService.StorageCustomerRoleName));

            var startDate = new DateTime(2026, 3, 15);
            await storageService.StartRentalAsync(unit.Id, customerId, unit.MonthlyRate,
                startDate, BillingFrequency.Monthly, startDate, adminId);

            var updatedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Occupied, updatedUnit!.Status);

            var refreshedCustomer = await userManager.FindByIdAsync(customerId);
            Assert.True(await userManager.IsInRoleAsync(refreshedCustomer!, StorageService.StorageCustomerRoleName));
        }

        [Fact]
        public async Task EndRentalAsync_RevertsUnitToAvailableAndMarksRentalEnded()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var (unit, customerId, adminId) = await CreatePropertyUnitAndCustomerAsync(scope, storageService, userManager);

            var startDate = new DateTime(2026, 3, 15);
            var rental = await storageService.StartRentalAsync(unit.Id, customerId, unit.MonthlyRate,
                startDate, BillingFrequency.Monthly, startDate, adminId);

            var endDate = startDate.AddMonths(6);
            await storageService.EndRentalAsync(rental.Id, endDate, adminId);

            var updatedUnit = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Available, updatedUnit!.Status);
            Assert.Null(await storageService.GetActiveRentalForUnitAsync(unit.Id));
        }

        [Fact]
        public async Task UpdateUnitAsync_DirectAttemptToSetOccupied_IsSilentlyIgnored()
        {
            using var scope = _fixture.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            Assert.NotNull(admin);

            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = "Unit Status Guard Test Property",
                AddressLine1 = "1 Test Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);

            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "GUARD-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = 100m
            }, admin.Id);

            // Never went through StartRentalAsync - a direct edit attempting to mark it
            // Occupied must be ignored and fall back to the unit's actual current status.
            unit.Status = StorageUnitStatus.Occupied;
            await storageService.UpdateUnitAsync(unit, admin.Id);

            var reloaded = await storageService.GetUnitByIdAsync(unit.Id);
            Assert.Equal(StorageUnitStatus.Available, reloaded!.Status);
        }

        private static async Task<(StorageUnit Unit, string CustomerId, string AdminId)> CreatePropertyUnitAndCustomerAsync(
            IServiceScope scope, IStorageService storageService, UserManager<ApplicationUser> userManager)
        {
            var admin = await userManager.FindByEmailAsync(DevTestAccounts.AdminEmail);
            Assert.NotNull(admin);

            var property = await storageService.CreatePropertyAsync(new StorageProperty
            {
                Name = $"Rental Test Property {Guid.NewGuid():N}",
                AddressLine1 = "1 Test Way",
                City = "Testville",
                State = "CA",
                PostalCode = "90000"
            }, admin!.Id);

            var unit = await storageService.CreateUnitAsync(new StorageUnit
            {
                StoragePropertyId = property.Id,
                UnitNumber = "RENTAL-01",
                LengthFeet = 10,
                WidthFeet = 20,
                HeightFeet = 8,
                MonthlyRate = 120m
            }, admin.Id);

            var customerEmail = $"rentaltest_{Guid.NewGuid():N}@patinablazor.local";
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
