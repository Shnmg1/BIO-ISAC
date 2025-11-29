namespace api.Models;

public class AuditLog
{
    public int Id { get; set; }
    public int? ThreatId { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Navigation properties (optional)
    public Threat? Threat { get; set; }
    public User? User { get; set; }
}


