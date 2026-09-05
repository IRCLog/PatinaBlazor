using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    public class StorageProperty : ISupportImageAttachments
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

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

        [StringLength(1000)]
        public string? Description { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public string? CreatedByUserId { get; set; }

        public string? ModifiedByUserId { get; set; }

        // Navigation properties
        public ApplicationUser? CreatedByUser { get; set; }
        public ApplicationUser? ModifiedByUser { get; set; }
        public ICollection<StorageUnit> Units { get; set; } = new List<StorageUnit>();
        public ICollection<ImageAttachment> Images { get; set; } = new List<ImageAttachment>();

        public const string ImageSubfolderName = "storageproperties";

        [NotMapped]
        public string ImageSubfolder => ImageSubfolderName;
    }
}
