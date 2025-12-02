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
                        new { text = "You are an expert analyst for the Bioeconomy sector. Your job is to analyze industry alerts for VALIDITY (is it true/reputable?) and URGENCY (impact on supply chain). Output strictly in JSON." }
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