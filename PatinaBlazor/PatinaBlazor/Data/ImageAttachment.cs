using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    // The single, uniform image record used by every entity that implements
    // ISupportImageAttachments - no more per-entity image classes (CollectableImage,
    // StoragePropertyImage, etc). A new owner type adds its own nullable FK column here.
    public class ImageAttachment
    {
        public int Id { get; set; }

        [Required]
        [StringLength(500)]
        public string FileName { get; set; } = string.Empty;

        // Relative to wwwroot, e.g. "/uploads/collectables/{guid}.jpg" - usable directly as
        // an <img src>, and (after trimming the leading slash) for Path.Combine when a file
        // operation like delete needs the physical location on disk.
        [Required]
        [StringLength(500)]
        public string RelativePath { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ThumbnailRelativePath { get; set; }

        [StringLength(500)]
        public string? MediumRelativePath { get; set; }

        [Required]
        [StringLength(100)]
        public string ContentType { get; set; } = string.Empty;

        [Required]
        public long FileSize { get; set; }

        [Required]
        public bool IsMainImage { get; set; } = false;

        [Required]
        public int DisplayOrder { get; set; } = 0;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Owner FKs - exactly one should be set (enforced by a DB check constraint).
        public Guid? CollectableId { get; set; }
        public Collectable? Collectable { get; set; }

        public int? StoragePropertyId { get; set; }
        public StorageProperty? StorageProperty { get; set; }

        // Falls back to the full-size image when a smaller variant wasn't generated
        // (e.g. an upload that couldn't be decoded and was saved as-is).
        [NotMapped]
        public string ThumbnailUrl => ThumbnailRelativePath ?? RelativePath;

        [NotMapped]
        public string MediumUrl => MediumRelativePath ?? RelativePath;
    }
}
