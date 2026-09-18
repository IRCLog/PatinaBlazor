namespace PatinaBlazor.Services
{
    public class BillingRunResult
    {
        public int RentalsProcessed { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
    }

    // The actual selection-and-charging logic behind the nightly billing job, pulled out of
    // StorageBillingHostedService (the BackgroundService/timer wrapper) so it's directly
    // callable and unit-testable without waiting on a real timer loop - same reasoning as
    // AdminDashboardService's extraction from Admin.razor's code-behind.
    public interface IStorageBillingService
    {
        // Charges every Active rental whose GetNextDueDate() is on or before asOf (defaults
        // to now) via IStoragePaymentService.ChargeRentalAsync. A failed charge leaves the
        // rental's LastBilledDate untouched, so it's automatically retried the next run -
        // no separate retry/backoff bookkeeping needed.
        Task<BillingRunResult> RunDueBillingAsync(DateTime? asOf = null);
    }
}
