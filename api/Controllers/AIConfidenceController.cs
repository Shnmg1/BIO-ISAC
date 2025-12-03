using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BioeconomyAlertSystem
{
    // 1. Define the structure of your desired output (The POCO)
    public class AlertAnalysis
    {
        [JsonPropertyName("validity_confidence")]
        public int ValidityConfidence { get; set; }

        [JsonPropertyName("urgency_score")]
        public int UrgencyScore { get; set; }

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; }

        [JsonPropertyName("key_entities")]
        public List<string> KeyEntities { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }
    }

    public class GeminiService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private const string ModelId = "gemini-1.5-pro"; // Or "gemini-1.5-flash"

        public GeminiService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
        }

        public async Task<AlertAnalysis> AnalyzeAlertAsync(string alertText)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={_apiKey}";

            // 2. Construct the Payload
            var requestBody = new
            {
                system_instruction = new
                {
                    parts = new[] {
                        new { text = @"You are an expert analyst for the Bioeconomy sector. Your job is to analyze industry alerts for VALIDITY (is it true/reputable?) and URGENCY (impact on supply chain). Output strictly in JSON.

URGENCY/SEVERITY GRADING RUBRIC - Use these definitions as the primary criteria for determining urgency_score:

LOW (Informational / Long-Term): News regarding early-stage R&D, academic studies, small pilot projects, or long-term policy discussions (5+ years out). These events have no immediate impact on price or supply availability.

MEDIUM (Watchlist / Regional): localized disruptions (weather/strikes) affecting a single facility, proposed regulations that are not yet law, or moderate price fluctuations (5-10%). These events affect local markets but do not threaten the global industry.

HIGH (Actionable / Market-Moving): Enacted legislation with future compliance deadlines, significant feedstock shortages affecting a whole region (e.g., ""all of Midwest""), or major M&A (mergers) shifting market power. These events require strategic planning within the quarter.

CRITICAL (Immediate / Catastrophic): Immediate regulatory bans effective <30 days, severe safety hazards (contamination/recalls), total supply chain stoppages (border closures/war), or insolvencies of major industry leaders. These events require crisis management today.

Map urgency/severity levels to urgency_score as follows:
- LOW: urgency_score 0-25
- MEDIUM: urgency_score 26-50
- HIGH: urgency_score 51-75
- CRITICAL: urgency_score 76-100

These rubric definitions should be used in conjunction with the specific examples provided in each analysis request." }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $@"
                                Analyze the following alert:
                                '{alertText}'

                                Use the urgency/severity grading rubric definitions (provided in system instructions) IN CONJUNCTION with the examples below to determine the appropriate urgency_score. The rubric provides general definitions for each urgency level, and the examples show how to apply these definitions to specific scenarios. Focus on Time Horizon and Operational Impact when making your assessment.

                                Reference Examples (use these alongside the rubric definitions):

                                LOW Severity Examples:
                                - ""University of Queensland researchers publish a paper on a new enzyme that breaks down plastic 5% faster in lab settings."" (Academic only. No commercial product yet. No impact on current supply chains.)
                                - ""Start-up 'GreenAlgae' receives $2M seed funding to build a demonstration unit in 2026."" (Too small and too far in the future to impact the current market.)

                                MEDIUM Severity Examples:
                                - ""Protests in France block access to the TotalEnergies La Mède biorefinery for 48 hours."" (Disruptive, but localized to one site and short-term. Supply chain can absorb this.)
                                - ""California legislators propose a bill to tax non-recycled paper packaging, voting scheduled for next year."" (It is a proposal, not a law. It allows time to prepare.)

                                HIGH Severity Examples:
                                - ""Brazil declares a state of emergency in key sugarcane regions; ethanol yield projected to drop 25% this season."" (Sugarcane is a primary feedstock. A 25% drop is a massive market shock that impacts global sugar and ethanol prices.)
                                - ""The EU Parliament passes the Deforestation-Free Products Regulation; companies must prove compliance by December 2025."" (This is now law, not a proposal. It forces companies to change their supply chains, but they have a grace period (not immediate crisis).)

                                CRITICAL Severity Examples:
                                - ""US FDA detects salmonella in 'BioFeed' synthetic protein shipments; issues immediate mandatory recall of all products sold in 2024."" (Immediate financial loss, legal risk, and brand damage. Stops sales instantly.)
                                - ""Panama Canal closes indefinitely to biomass carriers due to drought; all wood pellet shipments to Europe halted immediately."" (Total breakage of the logistics chain. There is no immediate alternative route without massive cost/delay.)

                                Return a JSON object with this schema:
                                {{
                                    ""validity_confidence"": integer (0-100),
                                    ""urgency_score"": integer (0-100),
                                    ""reasoning"": ""string"",
                                    ""key_entities"": [""string""],
                                    ""category"": ""string""
                                }}
                            " }
                        }
                    }
                },
                // 3. Enable JSON Mode
                generation_config = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.1
                },
                // 4. Enable Google Search Grounding (The "Validity" Check)
                tools = new[]
                {
                    new { google_search_retrieval = new { } }
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody), 
                Encoding.UTF8, 
                "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, jsonContent);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                
                // 5. Parse the Gemini Response structure to get the inner JSON
                using var doc = JsonDocument.Parse(responseString);
                var textResult = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                // Deserialize the inner JSON string into your C# Object
                return JsonSerializer.Deserialize<AlertAnalysis>(textResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Gemini API: {ex.Message}");
                return null; // Or handle appropriately
            }
        }
    }

    // Example Usage Program
    class Program
    {
        static async Task Main(string[] args)
        {
            string apiKey = "YOUR_API_KEY_HERE";
            var service = new GeminiService(apiKey);

            string alert = "New EU regulation bans all non-circular bioplastics effective immediately.";

            Console.WriteLine("Analyzing alert...");
            AlertAnalysis result = await service.AnalyzeAlertAsync(alert);

            if (result != null)
            {
                Console.WriteLine($"Category: {result.Category}");
                Console.WriteLine($"Validity: {result.ValidityConfidence}%");
                Console.WriteLine($"Urgency: {result.UrgencyScore}%");
                Console.WriteLine($"Reasoning: {result.Reasoning}");
            }
        }
    }
}