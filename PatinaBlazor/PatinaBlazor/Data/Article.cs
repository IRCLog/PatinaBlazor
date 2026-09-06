using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatinaBlazor.Data
{
    public class Article : ISupportImageAttachments
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(300)]
        public string? TagLine { get; set; }

        [Required]
        public string Body { get; set; } = string.Empty;

        [Required]
        public string AuthorUserId { get; set; } = string.Empty;

        public ArticleStatus Status { get; set; } = ArticleStatus.Draft;

        // Set once, the first time Status transitions to Published - preserved across
        // later edits/unpublish-republish cycles so it reflects original publish date.
        public DateTime? PublishedDate { get; set; }

        public ArticleAudience Audience { get; set; } = ArticleAudience.Public;

        public bool FeatureOnHomePage { get; set; }

        // Reserved for a future Storage-customer landing page (Phase 2) that doesn't
        // exist yet - not exposed in the authoring UI until that page is built.
        public bool FeatureOnStorageLanding { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public string? CreatedByUserId { get; set; }

        public string? ModifiedByUserId { get; set; }

        // Navigation properties
        public ApplicationUser? Author { get; set; }
        public ApplicationUser? CreatedByUser { get; set; }
        public ApplicationUser? ModifiedByUser { get; set; }
        public ICollection<ImageAttachment> Images { get; set; } = new List<ImageAttachment>();

        public const string ImageSubfolderName = "articles";

        [NotMapped]
        public string ImageSubfolder => ImageSubfolderName;
    }
}
