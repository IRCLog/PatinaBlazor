using System.ComponentModel.DataAnnotations;

namespace PatinaBlazor.Data
{
    /// <summary>
    /// Junction table for many-to-many relationship between Collectable and CollectableCollection
    /// </summary>
    public class CollectableCollectionItem
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid CollectableCollectionId { get; set; }

        [Required]
        public Guid CollectableId { get; set; }

        public DateTime AddedDate { get; set; }

        // Navigation properties
        public CollectableCollection Collection { get; set; } = null!;
        public Collectable Collectable { get; set; } = null!;
    }
}
