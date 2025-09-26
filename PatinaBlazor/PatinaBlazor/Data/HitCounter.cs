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
}