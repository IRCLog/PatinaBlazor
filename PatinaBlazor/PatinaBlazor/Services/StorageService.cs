using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class StorageService : IStorageService
    {
        public const string StorageCustomerRoleName = "Storage Customer";

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public StorageService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Properties

        public async Task<List<StorageProperty>> GetPropertiesAsync()
        {
            return await _context.StorageProperties
                .AsNoTracking()
                .Include(p => p.Units)
                .Include(p => p.Images)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<StorageProperty?> GetPropertyByIdAsync(int id)
        {
            return await _context.StorageProperties
                .Include(p => p.Units)
                .ThenInclude(u => u.Rentals)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<StorageProperty> CreatePropertyAsync(StorageProperty property, string currentUserId)
        {
            property.CreatedDate = DateTime.UtcNow;
            property.ModifiedDate = DateTime.UtcNow;
            property.CreatedByUserId = currentUserId;
            property.ModifiedByUserId = currentUserId;

            _context.StorageProperties.Add(property);
            await _context.SaveChangesAsync();
            return property;
        }

        public async Task UpdatePropertyAsync(StorageProperty property, string currentUserId)
        {
            property.ModifiedDate = DateTime.UtcNow;
            property.ModifiedByUserId = currentUserId;
            _context.StorageProperties.Update(property);
            await _context.SaveChangesAsync();
        }

        public async Task DeletePropertyAsync(int id)
        {
            var property = await _context.StorageProperties.FindAsync(id);
            if (property != null)
            {
                _context.StorageProperties.Remove(property);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<ImageAttachment> AddPropertyImageAsync(int propertyId, ImageUploadResult upload, bool isMainImage)
        {
            var image = new ImageAttachment
            {
                StoragePropertyId = propertyId,
                FileName = upload.FileName,
                RelativePath = upload.RelativePath,
                ThumbnailRelativePath = upload.ThumbnailRelativePath,
                MediumRelativePath = upload.MediumRelativePath,
                ContentType = upload.ContentType,
                FileSize = upload.FileSize,
                IsMainImage = isMainImage,
                DisplayOrder = await _context.ImageAttachments.CountAsync(i => i.StoragePropertyId == propertyId),
                CreatedDate = DateTime.UtcNow
            };

            _context.ImageAttachments.Add(image);
            await _context.SaveChangesAsync();
            return image;
        }

        public async Task<ImageAttachment?> GetPropertyImageAsync(int imageId)
        {
            return await _context.ImageAttachments.FindAsync(imageId);
        }

        public async Task DeletePropertyImageAsync(int imageId)
        {
            var image = await _context.ImageAttachments.FindAsync(imageId);
            if (image != null)
            {
                _context.ImageAttachments.Remove(image);
                await _context.SaveChangesAsync();
            }
        }

        // Units

        public async Task<List<StorageUnit>> GetUnitsForPropertyAsync(int propertyId)
        {
            return await _context.StorageUnits
                .AsNoTracking()
                .Include(u => u.Rentals)
                .Where(u => u.StoragePropertyId == propertyId)
                .OrderBy(u => u.UnitNumber)
                .ToListAsync();
        }

        public async Task<StorageUnit?> GetUnitByIdAsync(int id)
        {
            return await _context.StorageUnits
                .Include(u => u.Rentals)
                .Include(u => u.Property)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<StorageUnit> CreateUnitAsync(StorageUnit unit, string currentUserId)
        {
            unit.Status = StorageUnitStatus.Available;
            unit.CreatedDate = DateTime.UtcNow;
            unit.ModifiedDate = DateTime.UtcNow;
            unit.CreatedByUserId = currentUserId;
            unit.ModifiedByUserId = currentUserId;

            _context.StorageUnits.Add(unit);
            await _context.SaveChangesAsync();
            return unit;
        }

        public async Task UpdateUnitAsync(StorageUnit unit, string currentUserId)
        {
            // Occupied is exclusively a side effect of StartRentalAsync/EndRentalAsync, so a
            // direct edit can never set it - fall back to whatever status the unit already has.
            if (unit.Status == StorageUnitStatus.Occupied)
            {
                var existing = await _context.StorageUnits
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == unit.Id);
                unit.Status = existing?.Status ?? StorageUnitStatus.Available;
            }

            unit.ModifiedDate = DateTime.UtcNow;
            unit.ModifiedByUserId = currentUserId;
            _context.StorageUnits.Update(unit);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUnitAsync(int id)
        {
            var unit = await _context.StorageUnits.FindAsync(id);
            if (unit != null)
            {
                _context.StorageUnits.Remove(unit);
                await _context.SaveChangesAsync();
            }
        }

        // Rentals

        public async Task<StorageRental> StartRentalAsync(int unitId, string customerUserId, decimal monthlyRate, DateTime startDate, BillingFrequency billingFrequency, DateTime paymentDate, string currentUserId)
        {
            if (paymentDate.Date < startDate.Date || paymentDate.Date > startDate.Date.AddMonths(1))
            {
                throw new ArgumentException("Payment date must be on or after the start date, and no more than one month after it.", nameof(paymentDate));
            }

            var unit = await _context.StorageUnits.FirstOrDefaultAsync(u => u.Id == unitId)
                ?? throw new InvalidOperationException($"Storage unit {unitId} not found.");

            var rental = new StorageRental
            {
                StorageUnitId = unitId,
                CustomerUserId = customerUserId,
                StartDate = startDate,
                MonthlyRateAtSigning = monthlyRate,
                BillingFrequency = billingFrequency,
                PaymentDate = paymentDate,
                Status = StorageRentalStatus.Active,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                CreatedByUserId = currentUserId,
                ModifiedByUserId = currentUserId
            };

            _context.StorageRentals.Add(rental);
            unit.Status = StorageUnitStatus.Occupied;
            unit.ModifiedDate = DateTime.UtcNow;
            unit.ModifiedByUserId = currentUserId;

            await _context.SaveChangesAsync();

            var customer = await _userManager.FindByIdAsync(customerUserId);
            if (customer != null && !await _userManager.IsInRoleAsync(customer, StorageCustomerRoleName))
            {
                await _userManager.AddToRoleAsync(customer, StorageCustomerRoleName);
            }

            return rental;
        }

        public async Task EndRentalAsync(int rentalId, DateTime endDate, string currentUserId)
        {
            var rental = await _context.StorageRentals
                .Include(r => r.Unit)
                .FirstOrDefaultAsync(r => r.Id == rentalId)
                ?? throw new InvalidOperationException($"Storage rental {rentalId} not found.");

            rental.EndDate = endDate;
            rental.Status = StorageRentalStatus.Ended;
            rental.ModifiedDate = DateTime.UtcNow;
            rental.ModifiedByUserId = currentUserId;

            if (rental.Unit != null)
            {
                rental.Unit.Status = StorageUnitStatus.Available;
                rental.Unit.ModifiedDate = DateTime.UtcNow;
                rental.Unit.ModifiedByUserId = currentUserId;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<StorageRental?> GetActiveRentalForUnitAsync(int unitId)
        {
            return await _context.StorageRentals
                .Include(r => r.Customer)
                .Where(r => r.StorageUnitId == unitId && r.Status == StorageRentalStatus.Active)
                .OrderByDescending(r => r.StartDate)
                .FirstOrDefaultAsync();
        }

        // Dashboard aggregation

        public async Task<StorageDashboardSummary> GetDashboardSummaryAsync()
        {
            var totalProperties = await _context.StorageProperties.CountAsync();
            var units = await _context.StorageUnits.AsNoTracking().ToListAsync();
            var totalUnits = units.Count;
            var occupied = units.Count(u => u.Status == StorageUnitStatus.Occupied);
            var available = units.Count(u => u.Status == StorageUnitStatus.Available);
            var reserved = units.Count(u => u.Status == StorageUnitStatus.Reserved);
            var maintenance = units.Count(u => u.Status == StorageUnitStatus.Maintenance);

            var currentMrr = await _context.StorageRentals
                .Where(r => r.Status == StorageRentalStatus.Active)
                .SumAsync(r => (decimal?)r.MonthlyRateAtSigning) ?? 0m;

            return new StorageDashboardSummary
            {
                TotalProperties = totalProperties,
                TotalUnits = totalUnits,
                OccupiedUnits = occupied,
                AvailableUnits = available,
                ReservedUnits = reserved,
                MaintenanceUnits = maintenance,
                OccupancyPercent = totalUnits > 0 ? Math.Round(100.0 * occupied / totalUnits, 1) : 0,
                CurrentMrr = currentMrr,
                // Projection is deliberately a flat-line of current MRR - see GetMonthlyRevenueAsync.
                ProjectedNextMonthRevenue = currentMrr
            };
        }

        public async Task<List<MonthlyRevenuePoint>> GetMonthlyRevenueAsync(int monthsBack = 12, int monthsForwardProjection = 3)
        {
            var rentals = await _context.StorageRentals.AsNoTracking().ToListAsync();
            var points = new List<MonthlyRevenuePoint>();

            var thisMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

            for (var i = monthsBack - 1; i >= 0; i--)
            {
                var monthStart = thisMonthStart.AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);

                var revenue = rentals
                    .Where(r => r.StartDate < monthEnd && (r.EndDate ?? DateTime.MaxValue) >= monthStart)
                    .Sum(r => r.MonthlyRateAtSigning);

                points.Add(new MonthlyRevenuePoint
                {
                    MonthLabel = monthStart.ToString("MMM yyyy"),
                    Revenue = revenue,
                    IsProjected = false
                });
            }

            // Simple flat-line projection of current MRR - explicitly not a real forecast.
            var currentMrr = rentals
                .Where(r => r.Status == StorageRentalStatus.Active)
                .Sum(r => r.MonthlyRateAtSigning);

            for (var i = 1; i <= monthsForwardProjection; i++)
            {
                var monthStart = thisMonthStart.AddMonths(i);
                points.Add(new MonthlyRevenuePoint
                {
                    MonthLabel = monthStart.ToString("MMM yyyy"),
                    Revenue = currentMrr,
                    IsProjected = true
                });
            }

            return points;
        }

        // Dev seed hook

        public async Task SeedDummyDataAsync(string adminUserId, List<string> customerUserIds)
        {
            if (await _context.StorageProperties.AnyAsync())
            {
                return;
            }

            var now = DateTime.UtcNow;

            var properties = new List<StorageProperty>
            {
                new()
                {
                    Name = "Riverside RV & Boat Storage",
                    AddressLine1 = "4820 Riverside Dr",
                    City = "Bakersfield",
                    State = "CA",
                    PostalCode = "93301",
                    Description = "Gated outdoor storage yard along the river, close to the boat launch.",
                    CreatedDate = now,
                    ModifiedDate = now,
                    CreatedByUserId = adminUserId,
                    ModifiedByUserId = adminUserId
                },
                new()
                {
                    Name = "Highway 9 Storage Yard",
                    AddressLine1 = "1130 Highway 9",
                    City = "Bakersfield",
                    State = "CA",
                    PostalCode = "93308",
                    Description = "Large-vehicle storage yard with pull-through spaces for big rigs and RVs.",
                    CreatedDate = now,
                    ModifiedDate = now,
                    CreatedByUserId = adminUserId,
                    ModifiedByUserId = adminUserId
                }
            };

            _context.StorageProperties.AddRange(properties);
            await _context.SaveChangesAsync();

            var random = new Random(42);
            var unitSizes = new (decimal length, decimal width, decimal height, decimal rate)[]
            {
                (10, 20, 8, 85),
                (12, 24, 9, 110),
                (14, 30, 10, 150),
                (14, 35, 12, 195),
                (14, 40, 12, 245),
                (10, 25, 8, 95),
                (16, 40, 14, 275),
                (12, 30, 10, 140)
            };

            var allUnits = new List<StorageUnit>();

            foreach (var property in properties)
            {
                var unitCount = random.Next(6, 10);
                for (var i = 1; i <= unitCount; i++)
                {
                    var size = unitSizes[random.Next(unitSizes.Length)];
                    allUnits.Add(new StorageUnit
                    {
                        StoragePropertyId = property.Id,
                        UnitNumber = $"{property.Id}-{i:D2}",
                        LengthFeet = size.length,
                        WidthFeet = size.width,
                        HeightFeet = size.height,
                        MonthlyRate = size.rate,
                        Status = StorageUnitStatus.Available,
                        CreatedDate = now,
                        ModifiedDate = now,
                        CreatedByUserId = adminUserId,
                        ModifiedByUserId = adminUserId
                    });
                }
            }

            _context.StorageUnits.AddRange(allUnits);
            await _context.SaveChangesAsync();

            if (customerUserIds.Count == 0)
            {
                return;
            }

            // Give roughly two-thirds of the units a rental, spread over the past year,
            // with some already ended, so the dashboard's charts show real variation.
            var rentals = new List<StorageRental>();
            foreach (var unit in allUnits)
            {
                if (random.NextDouble() > 0.65)
                {
                    continue;
                }

                var customerId = customerUserIds[random.Next(customerUserIds.Count)];
                var startMonthsAgo = random.Next(1, 12);
                var startDate = now.AddMonths(-startMonthsAgo);
                var isEnded = startMonthsAgo > 3 && random.NextDouble() < 0.35;

                // Mostly monthly, with a realistic minority paying less often.
                var billingRoll = random.NextDouble();
                var billingFrequency = billingRoll < 0.7 ? BillingFrequency.Monthly
                    : billingRoll < 0.9 ? BillingFrequency.Quarterly
                    : BillingFrequency.Annually;

                var rental = new StorageRental
                {
                    StorageUnitId = unit.Id,
                    CustomerUserId = customerId,
                    StartDate = startDate,
                    MonthlyRateAtSigning = unit.MonthlyRate,
                    BillingFrequency = billingFrequency,
                    PaymentDate = startDate,
                    CreatedDate = startDate,
                    ModifiedDate = startDate,
                    CreatedByUserId = adminUserId,
                    ModifiedByUserId = adminUserId
                };

                if (isEnded)
                {
                    rental.EndDate = startDate.AddMonths(random.Next(1, startMonthsAgo));
                    rental.Status = StorageRentalStatus.Ended;
                }
                else
                {
                    rental.Status = StorageRentalStatus.Active;
                    unit.Status = StorageUnitStatus.Occupied;
                }

                rentals.Add(rental);
            }

            // Give a few of the still-vacant units a non-default status too, so the
            // occupancy donut isn't just a two-way Occupied/Available split.
            foreach (var unit in allUnits.Where(u => u.Status == StorageUnitStatus.Available))
            {
                var roll = random.NextDouble();
                if (roll < 0.1)
                {
                    unit.Status = StorageUnitStatus.Maintenance;
                }
                else if (roll < 0.2)
                {
                    unit.Status = StorageUnitStatus.Reserved;
                }
            }

            _context.StorageRentals.AddRange(rentals);
            await _context.SaveChangesAsync();
        }
    }
}
