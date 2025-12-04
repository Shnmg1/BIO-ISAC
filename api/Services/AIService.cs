using System.Text;
using System.Text.Json;
using api.Models;
using MyApp.Namespace.Services;

namespace api.Services;

public class AIService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIService> _logger;
    private readonly DatabaseService _dbService;

    public AIService(HttpClient httpClient, IConfiguration configuration, ILogger<AIService> logger, DatabaseService dbService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _dbService = dbService;
    }

    public async Task<ClassificationResult> ClassifyThreatAsync(Threat threat)
    {
        try
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || apiKey == "your_gemini_api_key_here")
            {
                _logger.LogWarning("Gemini API key not configured, returning default classification");
                // Return default classification when API key is not configured
                return new ClassificationResult
                {
                    Tier = ThreatTier.Medium,
                    Confidence = 50,
                    Reasoning = "AI classification pending - API key not configured. Manual review recommended.",
                    RecommendedActions = "Review threat manually and assign appropriate tier.",
                    NextSteps = new List<string> 
                    { 
                        "1. Review threat details manually - Security Team",
                        "2. Assign appropriate tier classification - Security Analyst",
                        "3. Configure AI API key for automated classification - IT Admin"
                    },
                    Keywords = new List<string>(),
                    BioSectorRelevance = 50,
                    RawResponse = "Default classification - API not configured"
                };
            }

            var model = _configuration["Gemini:Model"] ?? "gemini-1.5-pro";
            var prompt = BuildClassificationPrompt(threat);

            // Gemini API structure
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = prompt
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    topK = 40,
                    topP = 0.95,
                    maxOutputTokens = 4096,
                    responseMimeType = "application/json"
                }
            };

            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Gemini API error: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            // Parse Gemini response structure
            var content = jsonResponse
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            var classification = ParseAIResponse(content);

            return classification;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying threat with AI");
            // Return default classification on error instead of throwing
            return new ClassificationResult
            {
                Tier = ThreatTier.Medium,
                Confidence = 50,
                Reasoning = $"AI classification error: {ex.Message}. Manual review required.",
                RecommendedActions = "Review threat manually and assign appropriate tier.",
                NextSteps = new List<string> 
                { 
                    "1. Review threat details manually - Security Team",
                    "2. Investigate AI service error - IT Admin",
                    "3. Assign appropriate tier classification - Security Analyst"
                },
                Keywords = new List<string>(),
                BioSectorRelevance = 50,
                RawResponse = $"Error: {ex.Message}"
            };
        }
    }

    private string BuildClassificationPrompt(Threat threat)
    {
        return $@"You are a cybersecurity threat intelligence analyst specializing in biological and healthcare sector security.

Analyze the following threat submission and classify it according to the BioISAC risk matrix:

THREAT DETAILS:
Title: {threat.title}
Description: {threat.description}
Category: {threat.category}
Source: {threat.source}
Impact Level: {threat.impact_level}
Date Observed: {threat.date_observed:yyyy-MM-dd}

RISK MATRIX CRITERIA:

TIER 1 (High/Critical):
- Immediate threat to human life or biological safety
- Critical infrastructure compromise (hospitals, labs, manufacturing)
- Active data breach affecting patient/research data
- Ransomware affecting critical systems
- Supply chain compromise with biological impact
- Confidence: 80-100%

TIER 2 (Medium):
- Significant operational disruption
- Potential data exposure
- System vulnerabilities requiring attention
- Phishing campaigns targeting bio-sector
- Confidence: 50-79%

TIER 3 (Low):
- Informational alerts
- Non-critical vulnerabilities
- General security advisories
- Low-impact incidents
- Confidence: 0-49%

SUPPLY CHAIN CONSIDERATIONS:
- Medical device vulnerabilities
- Biomanufacturing equipment risks
- Lab equipment security
- Agriculture technology threats

HUMAN/BIOLOGICAL IMPACT SCORING:
- Direct patient safety impact: High tier
- Research data compromise: Medium-High tier
- Operational disruption: Medium tier
- Informational: Low tier

NEXT STEPS REQUIREMENTS:
- Provide 3-6 specific, actionable steps the company should take IMMEDIATELY
- Each step should be a discrete task that can be checked off
- Include responsible party when applicable (SOC, IT, Security Team, Management, Legal)
- All steps are immediate priority actions
- Order steps by execution sequence
- Reference standard incident response procedures where relevant
- For Tier 1: Focus on containment, escalation, and crisis management
- For Tier 2: Focus on investigation, patching, and monitoring
- For Tier 3: Focus on awareness, documentation, and routine updates

Respond ONLY with valid JSON in this exact format:
{{
    ""tier"": ""High"" | ""Medium"" | ""Low"",
    ""confidence"": 0-100,
    ""reasoning"": ""Detailed explanation of classification"",
    ""recommendedActions"": ""Brief summary of response strategy"",
    ""nextSteps"": [
        ""1. Action description - Responsible Party"",
        ""2. Action description - Responsible Party"",
        ""3. Action description - Responsible Party""
    ],
    ""keywords"": [""keyword1"", ""keyword2""],
    ""bioSectorRelevance"": 0-100
}}";
    }

    private ClassificationResult ParseAIResponse(string content)
    {
        try
        {
            // Extract JSON from response (handle markdown code blocks)
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}') + 1;
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                content = content.Substring(jsonStart, jsonEnd - jsonStart);
            }

            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var tierStr = root.GetProperty("tier").GetString() ?? "Low";
            var tier = Enum.Parse<ThreatTier>(tierStr, true);

            var confidence = root.GetProperty("confidence").GetDecimal();
            var reasoning = root.GetProperty("reasoning").GetString() ?? "";
            var recommendedActions = root.GetProperty("recommendedActions").GetString() ?? "";

            var keywords = new List<string>();
            if (root.TryGetProperty("keywords", out var keywordsElement))
            {
                foreach (var keyword in keywordsElement.EnumerateArray())
                {
                    keywords.Add(keyword.GetString() ?? "");
                }
            }

            var nextSteps = new List<string>();
            if (root.TryGetProperty("nextSteps", out var nextStepsElement))
            {
                foreach (var step in nextStepsElement.EnumerateArray())
                {
                    nextSteps.Add(step.GetString() ?? "");
                }
            }

            var bioSectorRelevance = root.TryGetProperty("bioSectorRelevance", out var relevanceElement) 
                ? relevanceElement.GetDecimal() 
                : 50;

            return new ClassificationResult
            {
                Tier = tier,
                Confidence = confidence,
                Reasoning = reasoning,
                RecommendedActions = recommendedActions,
                NextSteps = nextSteps,
                Keywords = keywords,
                BioSectorRelevance = bioSectorRelevance,
                RawResponse = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing AI response: {Content}", content);
            // Return default classification on parse error
            return new ClassificationResult
            {
                Tier = ThreatTier.Low,
                Confidence = 0,
                Reasoning = "AI response parsing failed",
                RecommendedActions = "Manual review required",
                NextSteps = new List<string> { "1. Review threat manually - Security Team" },
                Keywords = new List<string>(),
                BioSectorRelevance = 0,
                RawResponse = content
            };
        }
    }

    public async Task SaveClassificationAsync(int threatId, ClassificationResult result)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = @"INSERT INTO threat_analysis (threat_id, ai_tier, ai_confidence, ai_reasoning, ai_actions, ai_keywords, ai_classified_at, created_at) 
                         VALUES (@threat_id, @ai_tier, @ai_confidence, @ai_reasoning, @ai_actions, @ai_keywords, NOW(), NOW())";

            using var command = new MySqlConnector.MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@threat_id", threatId);
            command.Parameters.AddWithValue("@ai_tier", result.Tier.ToString());
            command.Parameters.AddWithValue("@ai_confidence", result.Confidence);
            command.Parameters.AddWithValue("@ai_reasoning", result.Reasoning);
            command.Parameters.AddWithValue("@ai_actions", JsonSerializer.Serialize(result.NextSteps));
            command.Parameters.AddWithValue("@ai_keywords", string.Join(",", result.Keywords));

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving classification");
            throw;
        }
    }
}

public class ClassificationResult
{
    public ThreatTier Tier { get; set; }
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string RecommendedActions { get; set; } = string.Empty;
    public List<string> NextSteps { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public decimal BioSectorRelevance { get; set; }
    public string RawResponse { get; set; } = string.Empty;
}

