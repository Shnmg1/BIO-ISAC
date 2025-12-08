namespace api.Models;

public class DocumentAnalysisResult
{
    public bool IsValid { get; set; }
    public double ConfidenceScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public Dictionary<string, string> ExtractedData { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

