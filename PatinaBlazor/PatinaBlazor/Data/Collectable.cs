using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    public class Collectable
    {
        public int Id { get; set; }

        [Required]
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Price paid must be a positive value")]
        public decimal PricePaid { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Asking price must be a positive value")]
        public decimal? AskingPrice { get; set; }

        public DateTime? DateAcquired { get; set; }

        [StringLength(200)]
        public string? AcquiredFrom { get; set; }

        public bool IsForSale { get; set; } = false;

        public bool IsSold { get; set; } = false;

        public DateTime? DateSold { get; set; }

        [StringLength(200)]
        public string? SoldTo { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Sale price must be a positive value")]
        public decimal? SalePrice { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Required]
        public string UserId { get; set; } = string.Empty;

        // Navigation properties
        public virtual ApplicationUser? User { get; set; }
        public virtual ICollection<CollectableImage> Images { get; set; } = new List<CollectableImage>();
    }
}