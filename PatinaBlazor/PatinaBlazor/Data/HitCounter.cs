using System.ComponentModel.DataAnnotations;

namespace PatinaBlazor.Data;

public class HitCounter
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string PagePath { get; set; } = string.Empty;
    
    public long HitCount { get; set; }
    
    public DateTime CreatedAt { get; set; }

    public DateTime LastHit { get; set; }

    // When set, this counter tracks a specific article's views instead of a page path.
    public Guid? ArticleId { get; set; }
    public Article? Article { get; set; }
}