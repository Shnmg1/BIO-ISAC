namespace api.Models;

public class ApiSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ApiKeyEncrypted { get; set; }
    public bool Enabled { get; set; }
    public FetchFrequency FetchFrequency { get; set; }
    public string? FilterKeywords { get; set; }
    public DateTime? LastSync { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum FetchFrequency
{
    Hourly,
    Daily,
    Weekly
}


