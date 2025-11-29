using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MyApp.Namespace.Services
{
    public class OTXService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://otx.alienvault.com/api/v1";

        public OTXService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["OTX:ApiKey"] ?? "4454bd50246c8265987afd9f6c73cf99000e18e73c7abfe1f6a4364d815b2201";
            _httpClient.DefaultRequestHeaders.Add("X-OTX-API-KEY", _apiKey);
        }

        public async Task<object?> GetThreatByHash(string hash)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/indicators/file/{hash}/general");
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<object>(content);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch threat data from OTX: {ex.Message}", ex);
            }
        }

        public async Task<object?> GetThreatPulses(string hash)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/indicators/file/{hash}/pulses");
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<object>(content);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch threat pulses from OTX: {ex.Message}", ex);
            }
        }

        public async Task<object?> GetThreatAnalysis(string hash)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/indicators/file/{hash}/analysis");
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<object>(content);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch threat analysis from OTX: {ex.Message}", ex);
            }
        }
    }
}

