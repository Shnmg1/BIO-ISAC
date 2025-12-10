namespace api.Models;

public class Notification
{
    public int Id { get; set; }
    public int? ThreatId { get; set; }
    public ThreatTier Tier { get; set; }
    public int? SentTo { get; set; }
    public FacilityType? SentToFacilityType { get; set; }
    public string? SentToIndustry { get; set; }
    public bool SentToAll { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DeliveryMethod DeliveryMethod { get; set; }
    public DeliveryStatus DeliveryStatus { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Threat? Threat { get; set; }
    public User? Recipient { get; set; }
}

public enum DeliveryMethod
{
    Email,
    InApp,
    Both
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Delivered,
    Failed,
    Bounced
}


