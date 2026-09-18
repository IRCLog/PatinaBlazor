using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    // One row per charge attempt against a rental - both successes and failures, so
    // billing history and admin visibility into repeated failures both come from the same
    // table. Written by IStoragePaymentService.ChargeRentalAsync, whether called
    // immediately at signup or by the nightly billing job.
    public class StoragePaymentTransaction
    {
        public int Id { get; set; }

        [Required]
        public int StorageRentalId { get; set; }

        // Null if the charge attempt failed before PayPal ever returned an order id.
        [StringLength(50)]
        public string? PayPalOrderId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Amount must be a positive value")]
        public decimal Amount { get; set; }

        [Required]
        public bool Succeeded { get; set; }

        [StringLength(500)]
        public string? FailureReason { get; set; }

        public DateTime OccurredAtUtc { get; set; }

        // Navigation properties
        public StorageRental? Rental { get; set; }
    }
}
