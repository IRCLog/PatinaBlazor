using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    public class StorageUnit
    {
        public int Id { get; set; }

        [Required]
        public int StoragePropertyId { get; set; }

        [Required]
        [StringLength(50)]
        public string UnitNumber { get; set; } = string.Empty;

        [Column(TypeName = "decimal(6,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Length must be a positive value")]
        public decimal LengthFeet { get; set; }

        [Column(TypeName = "decimal(6,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Width must be a positive value")]
        public decimal WidthFeet { get; set; }

        [Column(TypeName = "decimal(6,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Height must be a positive value")]
        public decimal HeightFeet { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Monthly rate must be a positive value")]
        public decimal MonthlyRate { get; set; }

        [Required]
        public StorageUnitStatus Status { get; set; } = StorageUnitStatus.Available;

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public string? CreatedByUserId { get; set; }

        public string? ModifiedByUserId { get; set; }

        // Navigation properties
        public StorageProperty? Property { get; set; }
        public ApplicationUser? CreatedByUser { get; set; }
        public ApplicationUser? ModifiedByUser { get; set; }
        public ICollection<StorageRental> Rentals { get; set; } = new List<StorageRental>();
    }
}
