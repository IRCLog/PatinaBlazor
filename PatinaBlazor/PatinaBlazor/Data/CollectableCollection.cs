using System.ComponentModel.DataAnnotations;

namespace PatinaBlazor.Data
{
    public class CollectableCollection
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public bool IsSystemCollection { get; set; } = false;

        // Navigation properties
        public ApplicationUser User { get; set; } = null!;
        public ICollection<CollectableCollectionItem> CollectableItems { get; set; } = new List<CollectableCollectionItem>();
    }
}
