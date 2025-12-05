using System.Text.Json;
using api.Models;

namespace api.Services;

public interface IDocumentVerificationService
{
    Task<DocumentAnalysisResult> AnalyzeDocumentsAsync(IFormFile workId, IFormFile? professionalLicense, IFormFile supervisorLetter);
}

public class GeminiDocumentVerificationService : IDocumentVerificationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiDocumentVerificationService> _logger;
    private readonly HttpClient _httpClient;

    public GeminiDocumentVerificationService(
        IConfiguration config,
        ILogger<GeminiDocumentVerificationService> logger,
        HttpClient httpClient)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentsAsync(
        IFormFile workId,
        IFormFile? professionalLicense,
        IFormFile supervisorLetter)
    {
        try
        {
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Gemini API key not configured");
                return CreateFailureResult("API configuration error");
            }

            // Convert files to base64
            var workIdBase64 = await ConvertToBase64(workId);
            var letterBase64 = await ConvertToBase64(supervisorLetter);
            string? licenseBase64 = null;
            
            if (professionalLicense != null)
            {
                licenseBase64 = await ConvertToBase64(professionalLicense);
            }

            // Build request parts
            var parts = new List<object>
            {
                new { text = BuildVerificationPrompt() },
                new { inline_data = new { mime_type = GetMimeType(workId), data = workIdBase64 } },
                new { inline_data = new { mime_type = GetMimeType(supervisorLetter), data = letterBase64 } }
            };

            if (licenseBase64 != null && professionalLicense != null)
            {
                parts.Add(new { inline_data = new { mime_type = GetMimeType(professionalLicense), data = licenseBase64 } });
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = parts.ToArray() }
                }
            };

            // Call Gemini API
            var response = await _httpClient.PostAsJsonAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}",
                requestBody
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Gemini API error: {error}");
                return CreateFailureResult("API request failed");
            }

            var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            return ParseGeminiResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing documents with Gemini");
            return CreateFailureResult("Analysis error occurred");
        }
    }

    private string BuildVerificationPrompt()
    {
        return @"You are an expert document verification system for BIO-ISAC (Bioeconomy Information Sharing and Analysis Center). Analyze these employment verification documents and provide a JSON response with this EXACT structure:

{
  ""isValid"": true/false,
  ""confidenceScore"": 0-100,
  ""reasoning"": ""detailed explanation of your decision"",
  ""extractedData"": {
    ""employeeName"": ""full name from documents"",
    ""employeeId"": ""work ID number"",
    ""organization"": ""organization/facility name"",
    ""department"": ""department/division"",
    ""supervisorName"": ""supervisor's name from letter"",
    ""licenseNumber"": ""professional license number or null""
  },
  ""warnings"": [""list any concerns or red flags""]
}

VERIFICATION CRITERIA:

1. Work ID authenticity:
   - Clear, readable text
   - Professional formatting
   - Contains employee photo (if applicable)
   - Visible ID number
   - Organization branding (hospital, lab, biotech company, agricultural facility)

2. Supervisor Letter validation:
   - On official letterhead
   - Contains supervisor signature
   - Includes contact information
   - Professional language
   - Dated recently (within last 90 days preferred)
   - Confirms employment in bioeconomy sector

3. Professional License (if provided):
   - Valid license number
   - Matches employee name
   - Not expired
   - Appropriate for claimed profession (medical, lab tech, agricultural, etc.)

4. Cross-document consistency:
   - Names match across all documents
   - Organization information is consistent
   - No contradictions

CONFIDENCE SCORING:
- 90-100: All documents authentic, clear, consistent, no concerns - AUTO APPROVE
- 70-89: Documents appear valid but minor quality issues - AUTO APPROVE
- 50-69: Acceptable documents but quality concerns - MANUAL REVIEW
- 30-49: Questionable authenticity, multiple red flags - MANUAL REVIEW
- 0-29: Clear signs of forgery, major inconsistencies - REJECT

Return ONLY the JSON object, no additional text or markdown formatting.";
    }

    private async Task<string> ConvertToBase64(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private string GetMimeType(IFormFile file)
    {
        return file.ContentType ?? "application/octet-stream";
    }

    private DocumentAnalysisResult ParseGeminiResponse(GeminiResponse? response)
    {
        try
        {
            if (response?.Candidates == null || response.Candidates.Length == 0)
            {
                return CreateFailureResult("No response from AI");
            }

            var content = response.Candidates[0].Content?.Parts?[0].Text;
            
            if (string.IsNullOrEmpty(content))
            {
                return CreateFailureResult("Empty response from AI");
            }

            // Clean up markdown code blocks if present
            content = content.Replace("```json", "").Replace("```", "").Trim();
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var analysis = JsonSerializer.Deserialize<DocumentAnalysisResult>(content, options);
            
            if (analysis == null)
            {
                return CreateFailureResult("Failed to parse AI response");
            }

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Gemini response");
            return CreateFailureResult("Failed to parse AI analysis");
        }
    }

    private DocumentAnalysisResult CreateFailureResult(string reason)
    {
        return new DocumentAnalysisResult
        {
            IsValid = false,
            ConfidenceScore = 0,
            Reasoning = $"Automatic verification failed: {reason}. Manual review required.",
            ExtractedData = new Dictionary<string, string>(),
            Warnings = new List<string> { reason }
        };
    }
}

// Response models for Gemini API
public class GeminiResponse
{
    public GeminiCandidate[]? Candidates { get; set; }
}

public class GeminiCandidate
{
    public GeminiContent? Content { get; set; }
}

public class GeminiContent
{
    public GeminiPart[]? Parts { get; set; }
}

public class GeminiPart
{
    public string? Text { get; set; }
}

