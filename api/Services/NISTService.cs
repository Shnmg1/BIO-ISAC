using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MyApp.Namespace.Services
{
    // Last verified working: 2025-01-XX
    // NVD API v2.0 endpoint: https://services.nvd.nist.gov/rest/json/cves/2.0
    // Known issues: NVD occasionally has maintenance windows, check https://nvd.nist.gov/developers/vulnerabilities
    
    public class TestConnectionResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public object? Data { get; set; }
    }

    public class NISTService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly ILogger<NISTService> _logger;
        private const string BaseUrl = "https://services.nvd.nist.gov/rest/json/cves/2.0";
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(6);
        private bool _isAvailable = true;

        public NISTService(HttpClient httpClient, IConfiguration configuration, ILogger<NISTService> logger)
        {
            _httpClient = httpClient;
            _apiKey = configuration["NIST:ApiKey"];
            _logger = logger;
        }

        public bool IsAvailable => _isAvailable;

        private async Task EnsureRateLimit()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest < _rateLimitDelay)
                {
                    var delayNeeded = _rateLimitDelay - timeSinceLastRequest;
                    _logger.LogWarning($"Rate limit: Waiting {delayNeeded.TotalSeconds:F1} seconds before next NVD request (no API key)");
                    await Task.Delay(delayNeeded);
                }
            }
            _lastRequestTime = DateTime.UtcNow;
        }

        private string BuildUrl(string basePath, Dictionary<string, string>? parameters = null, bool includeApiKey = true)
        {
            var url = basePath;
            var queryParams = new List<string>();

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    if (!string.IsNullOrEmpty(param.Value))
                    {
                        queryParams.Add($"{param.Key}={Uri.EscapeDataString(param.Value)}");
                    }
                }
            }

            if (includeApiKey && !string.IsNullOrEmpty(_apiKey))
            {
                queryParams.Add($"apiKey={Uri.EscapeDataString(_apiKey)}");
            }

            if (queryParams.Count > 0)
            {
                url += "?" + string.Join("&", queryParams);
            }

            return url;
        }

        public async Task<object?> GetCVE(string cveId)
        {
            if (!_isAvailable)
            {
                _logger.LogWarning("NVD API marked as unavailable - attempting connectivity test first");
                var testResult = await TestNVDConnection();
                if (!testResult.Success)
                {
                    throw new Exception($"NVD API is unavailable: {testResult.Error}");
                }
            }

            await EnsureRateLimit();

            var parameters = new Dictionary<string, string>
            {
                { "cveId", cveId }
            };

            var url = BuildUrl(BaseUrl, parameters);
            var result = await ExecuteRequestWithRetry(url, maxRetries: 1);
            
            if (!result.Success)
            {
                if (result.Error?.Contains("404") == true || result.Error?.Contains("Not Found") == true)
                {
                    throw new Exception($"CVE not found: {cveId}. Response: {result.Error}");
                }
                if (result.Error?.Contains("403") == true || result.Error?.Contains("Forbidden") == true)
                {
                    throw new Exception($"NVD API authentication failed. Check API key. Response: {result.Error}");
                }
                if (result.Error?.Contains("429") == true || result.Error?.Contains("Too Many Requests") == true)
                {
                    throw new Exception($"NVD API rate limit exceeded. Response: {result.Error}");
                }
                
                throw new Exception($"Failed to fetch CVE data from NIST: {result.Error}. URL: {url}");
            }

            return result.Data;
        }

        public async Task<object?> GetCVEs(int resultsPerPage = 20, int startIndex = 0, string? keywordSearch = null, string? pubStartDate = null, string? pubEndDate = null)
        {
            if (!_isAvailable)
            {
                _logger.LogWarning("NVD API marked as unavailable - attempting connectivity test first");
                var testResult = await TestNVDConnection();
                if (!testResult.Success)
                {
                    throw new Exception($"NVD API is unavailable: {testResult.Error}");
                }
            }

            await EnsureRateLimit();

            var parameters = new Dictionary<string, string>
            {
                { "resultsPerPage", resultsPerPage.ToString() },
                { "startIndex", startIndex.ToString() }
            };

            if (!string.IsNullOrEmpty(keywordSearch))
            {
                parameters.Add("keywordSearch", keywordSearch);
            }
            if (!string.IsNullOrEmpty(pubStartDate))
            {
                parameters.Add("pubStartDate", pubStartDate);
            }
            if (!string.IsNullOrEmpty(pubEndDate))
            {
                parameters.Add("pubEndDate", pubEndDate);
            }

            var url = BuildUrl(BaseUrl, parameters);
            var result = await ExecuteRequestWithRetry(url, maxRetries: 1);
            
            if (!result.Success)
            {
                if (result.Error?.Contains("404") == true || result.Error?.Contains("Not Found") == true)
                {
                    throw new Exception($"NVD API endpoint not found. Check URL structure. Response: {result.Error}. URL: {url}");
                }
                if (result.Error?.Contains("403") == true || result.Error?.Contains("Forbidden") == true)
                {
                    throw new Exception($"NVD API authentication failed. Check API key. Response: {result.Error}");
                }
                if (result.Error?.Contains("429") == true || result.Error?.Contains("Too Many Requests") == true)
                {
                    throw new Exception($"NVD API rate limit exceeded. Response: {result.Error}");
                }
                
                throw new Exception($"Failed to fetch CVEs from NIST: {result.Error}. URL: {url}");
            }

            return result.Data;
        }

        public async Task<TestConnectionResult> TestNVDConnection()
        {
            _logger.LogInformation("Testing NVD API connectivity with minimal request...");
            
            // Try multiple URL formats and parameter combinations
            // Start with the format we know works (without API key)
            var testConfigs = new[]
            {
                new { Base = BaseUrl, Params = new Dictionary<string, string> { { "resultsPerPage", "5" } }, Name = "Standard without API key (known working)", UseApiKey = false },
                new { Base = BaseUrl, Params = new Dictionary<string, string> { { "resultsPerPage", "5" } }, Name = "Standard with API key", UseApiKey = true },
                new { Base = BaseUrl, Params = new Dictionary<string, string> { { "resultsPerPage", "1" } }, Name = "Minimal without API key", UseApiKey = false },
                new { Base = BaseUrl + "/", Params = new Dictionary<string, string> { { "resultsPerPage", "5" } }, Name = "With trailing slash", UseApiKey = false },
                new { Base = "https://services.nvd.nist.gov/rest/json/cves/2.0", Params = new Dictionary<string, string> { { "resultsPerPage", "5" } }, Name = "Explicit URL", UseApiKey = false }
            };
            
            foreach (var config in testConfigs)
            {
                try
                {
                    await EnsureRateLimit();
                    
                    // Build URL - conditionally include API key based on config
                    var testUrl = BuildUrl(config.Base, config.Params, includeApiKey: config.UseApiKey);
                    _logger.LogInformation($"Testing NVD connectivity - {config.Name}: {testUrl}");
                    
                    var response = await _httpClient.GetAsync(testUrl);
                    var content = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation($"NVD Connectivity Test - URL: {testUrl}");
                    _logger.LogInformation($"NVD Connectivity Test - Status: {response.StatusCode}, Content Length: {content.Length}, Has Content: {!string.IsNullOrWhiteSpace(content)}");
                    
                    // Log response headers for debugging
                    _logger.LogInformation($"Response Headers - Content-Type: {response.Content.Headers.ContentType}, Status: {response.StatusCode}");
                
                    if (response.IsSuccessStatusCode)
                    {
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            _logger.LogWarning($"NVD API returned success but with empty response body for URL: {testUrl}");
                            continue; // Try next URL format
                        }
                        
                        try
                        {
                            var json = JsonSerializer.Deserialize<object>(content);
                            _logger.LogInformation($"✅ NVD API connectivity test PASSED - Working URL: {testUrl}");
                            _isAvailable = true;
                            return new TestConnectionResult
                            {
                                Success = true,
                                Error = null,
                                Data = json
                            };
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, $"NVD API returned invalid JSON for URL: {testUrl}");
                            continue; // Try next URL format
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning($"NVD API returned 404 for {config.Name} - URL: {testUrl} - trying next format...");
                        if (config == testConfigs.Last())
                        {
                            // Last attempt failed, return error
                            _isAvailable = false;
                            return new TestConnectionResult
                            {
                                Success = false,
                                Error = $"NVD API endpoint not found. Tried {testConfigs.Length} different URL formats, all returned 404. API may be down or endpoint structure changed. Check https://nvd.nist.gov/developers/vulnerabilities for status. Last attempted URL: {testUrl}",
                                Data = null
                            };
                        }
                        continue; // Try next URL format
                    }
                    else
                    {
                        // Other error codes - return immediately
                        string errorMsg;
                        
                        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            errorMsg = "NVD API authentication failed. Check API key.";
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            errorMsg = "NVD API rate limit exceeded.";
                        }
                        else
                        {
                            errorMsg = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                        }
                        
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            errorMsg += " (empty response body)";
                        }
                        else
                        {
                            errorMsg += $" - Response: {(content.Length > 200 ? content.Substring(0, 200) + "..." : content)}";
                        }
                        
                        _logger.LogError($"NVD API connectivity test FAILED - {errorMsg}");
                        
                        return new TestConnectionResult
                        {
                            Success = false,
                            Error = errorMsg,
                            Data = null
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Exception testing {config.Name} - URL: {config.Base} - trying next...");
                    continue; // Try next URL format
                }
            }
            
            // If we get here, all URL formats failed
            _logger.LogError("NVD API connectivity test FAILED - All URL formats returned 404 or errors");
            _isAvailable = false;
            return new TestConnectionResult
            {
                Success = false,
                Error = "NVD API endpoint not found. Tried multiple URL formats. API may be down, endpoint changed, or URL structure incorrect. Check https://nvd.nist.gov/developers/vulnerabilities for status.",
                Data = null
            };
        }

        private async Task<(bool Success, object? Data, string? Error)> ExecuteRequestWithRetry(string url, int maxRetries = 1)
        {
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    _logger.LogWarning($"Retrying NVD API request (attempt {attempt + 1}/{maxRetries + 1}) after 10 second delay...");
                    await Task.Delay(10000);
                }

                try
                {
                    _logger.LogInformation($"NVD API Request (attempt {attempt + 1}): {url}");
                    
                    var response = await _httpClient.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogInformation($"NVD API Response - Status: {response.StatusCode}, Content Length: {content.Length}");
                    _logger.LogDebug($"NVD API Response Body: {(content.Length > 500 ? content.Substring(0, 500) + "..." : content)}");

                    if (response.IsSuccessStatusCode)
                    {
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            var error = "Response body is empty";
                            _logger.LogError($"NVD API Error: {error}");
                            
                            if (attempt < maxRetries)
                            {
                                continue;
                            }
                            
                            return (false, null, error);
                        }

                        try
                        {
                            var json = JsonSerializer.Deserialize<object>(content);
                            _isAvailable = true;
                            return (true, json, null);
                        }
                        catch (JsonException ex)
                        {
                            var error = $"Failed to deserialize JSON: {ex.Message}";
                            _logger.LogError(ex, $"NVD API JSON Deserialization Error: {error}");
                            
                            if (attempt < maxRetries)
                            {
                                continue;
                            }
                            
                            return (false, null, error);
                        }
                    }
                    else
                    {
                        var error = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            error += " (empty response body)";
                        }
                        else
                        {
                            error += $" - Response: {(content.Length > 200 ? content.Substring(0, 200) + "..." : content)}";
                        }
                        
                        _logger.LogError($"NVD API Error: {error}");

                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && attempt < maxRetries)
                        {
                            _logger.LogWarning("404 Not Found - will retry after delay");
                            continue;
                        }

                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            _isAvailable = false;
                        }

                        return (false, null, error);
                    }
                }
                catch (HttpRequestException ex)
                {
                    var error = $"HTTP Request Exception: {ex.Message}";
                    _logger.LogError(ex, $"NVD API Request Exception: {error}");
                    
                    if (attempt < maxRetries)
                    {
                        continue;
                    }
                    
                    return (false, null, error);
                }
                catch (Exception ex)
                {
                    var error = $"Unexpected Exception: {ex.Message}";
                    _logger.LogError(ex, $"NVD API Unexpected Exception: {error}");
                    
                    if (attempt < maxRetries)
                    {
                        continue;
                    }
                    
                    return (false, null, error);
                }
            }

            return (false, null, "Max retries exceeded");
        }

        public async Task<object?> FetchNVDCVEs(string keyword)
        {
            if (!_isAvailable)
            {
                _logger.LogWarning("NVD API marked as unavailable - attempting connectivity test first");
                var testResult = await TestNVDConnection();
                if (!testResult.Success)
                {
                    throw new Exception($"NVD API is unavailable: {testResult.Error}");
                }
            }

            await EnsureRateLimit();

            var parameters = new Dictionary<string, string>
            {
                { "resultsPerPage", "100" },
                { "startIndex", "0" }
            };

            // Try WITHOUT API key first (we know this works)
            var urlWithoutKey = BuildUrl(BaseUrl, parameters, includeApiKey: false);
            _logger.LogInformation($"FetchNVDCVEs - Trying without API key: {urlWithoutKey}");

            var resultWithoutKey = await ExecuteRequestWithRetry(urlWithoutKey, maxRetries: 0);
            
            if (resultWithoutKey.Success)
            {
                _logger.LogInformation("✅ FetchNVDCVEs - Success without API key");
                return resultWithoutKey.Data;
            }
            
            _logger.LogWarning($"⚠️ FetchNVDCVEs - Request without API key failed: {resultWithoutKey.Error}. Trying with API key...");

            // Fallback: Try with API key
            var urlWithKey = BuildUrl(BaseUrl, parameters, includeApiKey: true);
            _logger.LogInformation($"FetchNVDCVEs - Trying with API key: {urlWithKey}");

            var resultWithKey = await ExecuteRequestWithRetry(urlWithKey, maxRetries: 1);
            
            if (!resultWithKey.Success)
            {
                throw new Exception($"Failed to fetch CVEs from NVD: {resultWithKey.Error}. Tried with and without API key. Last attempted URL: {urlWithKey}");
            }

            _logger.LogInformation("✅ FetchNVDCVEs - Success with API key");
            return resultWithKey.Data;
        }

        public async Task<object?> GetCPEMatch(string cpeName)
        {
            if (!_isAvailable)
            {
                _logger.LogWarning("NVD API marked as unavailable - attempting connectivity test first");
                var testResult = await TestNVDConnection();
                if (!testResult.Success)
                {
                    throw new Exception($"NVD API is unavailable: {testResult.Error}");
                }
            }

            await EnsureRateLimit();

            var basePath = "https://services.nvd.nist.gov/rest/json/cpeMatch/2.0";
            var parameters = new Dictionary<string, string>
            {
                { "cpeName", cpeName }
            };

            var url = BuildUrl(basePath, parameters);
            var result = await ExecuteRequestWithRetry(url, maxRetries: 1);
            
            if (!result.Success)
            {
                if (result.Error?.Contains("404") == true || result.Error?.Contains("Not Found") == true)
                {
                    throw new Exception($"CPE match not found. Response: {result.Error}");
                }
                if (result.Error?.Contains("403") == true || result.Error?.Contains("Forbidden") == true)
                {
                    throw new Exception($"NVD API authentication failed. Check API key. Response: {result.Error}");
                }
                if (result.Error?.Contains("429") == true || result.Error?.Contains("Too Many Requests") == true)
                {
                    throw new Exception($"NVD API rate limit exceeded. Response: {result.Error}");
                }
                
                throw new Exception($"Failed to fetch CPE match data from NIST: {result.Error}. URL: {url}");
            }

            return result.Data;
        }
    }
}

