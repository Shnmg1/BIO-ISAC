namespace api.Models;

public class Message
{
    public int Id { get; set; }
    public int FromUserId { get; set; }
    public int ToUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? ThreatId { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public User? FromUser { get; set; }
    public User? ToUser { get; set; }
    public Threat? Threat { get; set; }
}


