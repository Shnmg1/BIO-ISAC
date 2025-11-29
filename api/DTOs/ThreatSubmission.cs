using System.ComponentModel.DataAnnotations;

namespace api.DTOs;

public class ThreatSubmission
{
    [Required(ErrorMessage = "Title is required")]
    [StringLength(500, ErrorMessage = "Title must be 500 characters or less")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(10000, ErrorMessage = "Description must be 10,000 characters or less")]
    public string Description { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string? Source { get; set; }

    public DateTime? DateObserved { get; set; }

    public string? ImpactLevel { get; set; }
}


