namespace api.DTOs;

public class AuditLogFilter
{
    public int? ThreatId { get; set; }
    public int? UserId { get; set; }
    public string? ActionType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; } = 0;
}

