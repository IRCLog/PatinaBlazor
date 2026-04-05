using System.ComponentModel.DataAnnotations;

namespace PatinaBlazor.Data;

public class IrcEvent
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime Timestamp { get; set; }

    [Required]
    public ChatAction Action { get; set; }

    [StringLength(4000)]
    public string? Message { get; set; }

    [StringLength(200)]
    public string? Target { get; set; }

    [Required]
    [StringLength(100)]
    public string Network { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Channel { get; set; }

    [StringLength(100)]
    public string? Sender { get; set; }

    [StringLength(100)]
    public string? User { get; set; }

    public DateTime CreatedDate { get; set; }
}
