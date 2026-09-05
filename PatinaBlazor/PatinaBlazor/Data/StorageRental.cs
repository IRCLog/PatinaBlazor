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

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public string? CreatedByUserId { get; set; }

        public string? ModifiedByUserId { get; set; }

        // Navigation properties
        public StorageUnit? Unit { get; set; }
        public ApplicationUser? Customer { get; set; }
        public ApplicationUser? CreatedByUser { get; set; }
        public ApplicationUser? ModifiedByUser { get; set; }
    }
}
