using System.ComponentModel.DataAnnotations;

namespace PatinaBlazor.Data
{
    // A 1:1 extension of ApplicationUser holding fields that are meaningful only for storage
    // customers (address, emergency contact) - not bolted onto ApplicationUser itself, unlike
    // DisplayName/PhoneNumber (both generically useful for any account type). Shared-primary-key
    // relationship: UserId is both this table's PK and its FK to AspNetUsers.
    public class StorageCustomerProfile
    {
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        [Required]
        [StringLength(200)]
        public string AddressLine1 { get; set; } = string.Empty;

        [StringLength(200)]
        public string? AddressLine2 { get; set; }

        [Required]
        [StringLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string State { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string PostalCode { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string EmergencyContactName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string EmergencyContactPhone { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; }
    }
}
