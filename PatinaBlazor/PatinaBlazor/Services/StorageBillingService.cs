using Microsoft.EntityFrameworkCore;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class StorageBillingService : IStorageBillingService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IStoragePaymentService _paymentService;
        private readonly ILogger<StorageBillingService> _logger;

        public StorageBillingService(IDbContextFactory<ApplicationDbContext> contextFactory, IStoragePaymentService paymentService, ILogger<StorageBillingService> logger)
        {
            _contextFactory = contextFactory;
            _paymentService = paymentService;
            _logger = logger;
        }

        public async Task<BillingRunResult> RunDueBillingAsync(DateTime? asOf = null)
        {
            var cutoff = (asOf ?? DateTime.UtcNow).Date;

            List<int> dueRentalIds;
            await using (var context = await _contextFactory.CreateDbContextAsync())
            {
                // GetNextDueDate() isn't SQL-translatable (it's C# logic over PaymentDate/
                // LastBilledDate/BillingFrequency) - candidate rentals are materialized
                // first, then filtered in memory. Fine at this app's real scale (a personal
                // storage business, not a nationwide chain).
                //
                // PaymentIssue rentals are included alongside Active ones, not just Active -
                // PaymentIssue is exactly "still owed money, last attempt failed", and this
                // run is the entire retry mechanism (a failed ChargeRentalAsync call leaves
                // LastBilledDate untouched, so GetNextDueDate() still reports it due). Only
                // querying Active would silently stop retrying the moment the first attempt
                // failed - the opposite of the intended behavior.
                var billableRentals = await context.StorageRentals
                    .AsNoTracking()
                    .Where(r => r.Status == StorageRentalStatus.Active || r.Status == StorageRentalStatus.PaymentIssue)
                    .ToListAsync();

                dueRentalIds = billableRentals
                    .Where(r => r.GetNextDueDate().Date <= cutoff)
                    .Select(r => r.Id)
                    .ToList();
            }

            var succeeded = 0;
            var failed = 0;

            foreach (var rentalId in dueRentalIds)
            {
                try
                {
                    var result = await _paymentService.ChargeRentalAsync(rentalId);
                    if (result.Succeeded)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                        _logger.LogWarning("Nightly storage billing: charge failed for rental {RentalId}: {Error}", rentalId, result.Error);
                    }
                }
                catch (Exception ex)
                {
                    // A charge attempt throwing (rather than returning Succeeded=false) must
                    // not stop the rest of the night's run - one bad rental shouldn't block
                    // everyone else's charge.
                    failed++;
                    _logger.LogError(ex, "Nightly storage billing: charging rental {RentalId} threw.", rentalId);
                }
            }

            _logger.LogInformation("Nightly storage billing run: {Processed} due, {Succeeded} succeeded, {Failed} failed.",
                dueRentalIds.Count, succeeded, failed);

            return new BillingRunResult { RentalsProcessed = dueRentalIds.Count, Succeeded = succeeded, Failed = failed };
        }
    }
}
