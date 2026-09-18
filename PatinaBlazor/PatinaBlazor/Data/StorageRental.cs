using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    public class StorageRental
    {
        public int Id { get; set; }

        [Required]
        public int StorageUnitId { get; set; }

        [Required]
        public string CustomerUserId { get; set; } = string.Empty;

        [Required]
        public DateTime StartDate { get; set; }

        // Null means the rental is still active.
        public DateTime? EndDate { get; set; }

        // Snapshot of the unit's monthly rate at the time the rental started, so
        // historical revenue stays accurate even if the unit's rate changes later.
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Monthly rate must be a positive value")]
        public decimal MonthlyRateAtSigning { get; set; }

        [Required]
        public StorageRentalStatus Status { get; set; } = StorageRentalStatus.Active;

        // How often the customer is actually invoiced - independent of MonthlyRateAtSigning,
        // which stays a monthly-equivalent figure for revenue/MRR reporting regardless of cadence.
        [Required]
        public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Monthly;

        // The billing anchor: every recurring charge lands on this same day-of-month (Monthly),
        // every 3rd month (Quarterly), or the same date each year (Annually). Must fall within
        // [StartDate, StartDate + 1 month] - enforced in StorageService.StartRentalAsync.
        [Required]
        public DateTime PaymentDate { get; set; }

        // The date the most recent successful charge covered - null until the first charge
        // succeeds. Distinct from PaymentDate (the fixed anchor) - this is what the nightly
        // billing job actually advances, via GetNextDueDate() below.
        public DateTime? LastBilledDate { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public string? CreatedByUserId { get; set; }

        public string? ModifiedByUserId { get; set; }

        // Navigation properties
        public StorageUnit? Unit { get; set; }
        public ApplicationUser? Customer { get; set; }
        public ApplicationUser? CreatedByUser { get; set; }
        public ApplicationUser? ModifiedByUser { get; set; }

        // The next date on/after asOf that a charge is due, preserving PaymentDate's original
        // day-of-month across cycles. Always computed as PaymentDate.AddMonths(cycles * period) -
        // never by repeatedly adding a period to the previous cycle's date - so a Jan 31 anchor
        // stays anchored to the 31st (e.g. Mar 31) instead of permanently drifting to the 28th
        // the first time a short month (Feb) clamps it. DateTime.AddMonths already handles
        // variable month lengths and leap years correctly, so no custom calendar math is needed.
        // How many months one billing cycle covers - shared by the due-date math below and by
        // GetChargeAmount(), so the "quarterly means 3 months" mapping only lives in one place.
        public int GetBillingPeriodMonths() => BillingFrequency switch
        {
            BillingFrequency.Quarterly => 3,
            BillingFrequency.Annually => 12,
            _ => 1
        };

        // The amount due for one billing cycle - MonthlyRateAtSigning scaled to the cycle
        // length, e.g. a $110/mo unit billed Annually charges $1,320 once a year.
        public decimal GetChargeAmount() => MonthlyRateAtSigning * GetBillingPeriodMonths();

        public DateTime GetNextBillingDate(DateTime asOf)
        {
            var periodMonths = GetBillingPeriodMonths();

            if (asOf <= PaymentDate)
            {
                return PaymentDate;
            }

            var monthsElapsed = ((asOf.Year - PaymentDate.Year) * 12) + (asOf.Month - PaymentDate.Month);
            var cycles = monthsElapsed / periodMonths;
            var candidate = PaymentDate.AddMonths(cycles * periodMonths);

            while (candidate < asOf)
            {
                cycles++;
                candidate = PaymentDate.AddMonths(cycles * periodMonths);
            }

            return candidate;
        }

        // The next date this rental actually still owes a charge for - distinct from
        // GetNextBillingDate(asOf), which just answers "what's the smallest valid cycle
        // date on or after asOf" with no memory of what's already been paid. Passing the
        // day *after* LastBilledDate (not the date itself) as asOf is what makes the
        // cycle just billed excluded from the result, since that date itself would
        // otherwise come right back as "the smallest valid cycle date >= asOf".
        public DateTime GetNextDueDate() =>
            GetNextBillingDate(LastBilledDate?.AddDays(1) ?? PaymentDate);
    }
}
