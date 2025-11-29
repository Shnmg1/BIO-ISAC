using System.Text.Json;

namespace MyApp.Namespace.Services
{
    public class CISAService
    {
        private readonly HttpClient _httpClient;
        private const string FeedUrl = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";

        public CISAService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<object?> GetKnownExploitedVulnerabilities()
        {
            try
            {
                var response = await _httpClient.GetAsync(FeedUrl);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<object>(content);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch CISA known exploited vulnerabilities: {ex.Message}", ex);
            }
        }

        public async Task<object?> GetVulnerabilityByCVE(string cveId)
        {
            try
            {
                var response = await _httpClient.GetAsync(FeedUrl);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);
                
                if (data.TryGetProperty("vulnerabilities", out var vulnerabilities))
                {
                    foreach (var vuln in vulnerabilities.EnumerateArray())
                    {
                        if (vuln.TryGetProperty("cveID", out var cve) && 
                            cve.GetString()?.Equals(cveId, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            return JsonSerializer.Deserialize<object>(vuln.GetRawText());
                        }
                    }
                }
                
                return null;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch CISA vulnerability data: {ex.Message}", ex);
            }
        }

        public async Task<object?> GetVulnerabilitiesByVendor(string vendor)
        {
            try
            {
                var response = await _httpClient.GetAsync(FeedUrl);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);
                
                var results = new List<object>();
                
                if (data.TryGetProperty("vulnerabilities", out var vulnerabilities))
                {
                    foreach (var vuln in vulnerabilities.EnumerateArray())
                    {
                        if (vuln.TryGetProperty("vendorProject", out var vendorProp) && 
                            vendorProp.GetString()?.Contains(vendor, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            results.Add(JsonSerializer.Deserialize<object>(vuln.GetRawText())!);
                        }
                    }
                }
                
                return results;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch CISA vulnerability data: {ex.Message}", ex);
            }
        }
    }
}

