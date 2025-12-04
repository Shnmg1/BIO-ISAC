namespace api.Models;

public class Classification
{
    public int Id { get; set; }
    public int ThreatId { get; set; }
    public ThreatTier? AiTier { get; set; }
    public decimal? AiConfidence { get; set; }
    public string? AiReasoning { get; set; }
    public string? AiActions { get; set; }
    public string? AiNextSteps { get; set; }  // JSON array of next steps
    public ThreatTier? HumanTier { get; set; }
    public HumanDecision? HumanDecision { get; set; }
    public string? HumanJustification { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties (optional)
    public Threat? Threat { get; set; }
    public User? Reviewer { get; set; }
}

public enum ThreatTier
{
    High,
    Medium,
    Low
}

public enum HumanDecision
{
    Approved,
    Override,
    False_Positive
}


