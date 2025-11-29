using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyApp.Namespace.Services;

namespace MyApp.Namespace.Services
{
    public class ApiValidationService
    {
        private readonly OTXService _otxService;
        private readonly NISTService _nistService;
        private readonly CISAService _cisaService;
        private readonly ILogger<ApiValidationService> _logger;

        private readonly string[] _bioEconomyKeywords = new[]
        {
            "medical", "healthcare", "hospital", "lab", "laboratory", "pharmaceutical",
            "pharma", "scada", "ics", "biotech", "biotechnology", "clinical", "patient",
            "diagnostic", "therapeutic", "biomedical", "health", "care", "device"
        };

        public ApiValidationService(
            OTXService otxService,
            NISTService nistService,
            CISAService cisaService,
            ILogger<ApiValidationService> logger)
        {
            _otxService = otxService;
            _nistService = nistService;
            _cisaService = cisaService;
            _logger = logger;
        }

        public async Task<ValidationReport> TestAllAPIs()
        {
            var report = new ValidationReport
            {
                Timestamp = DateTime.UtcNow,
                Results = new Dictionary<string, ApiTestResult>()
            };

            LogMessage("=".PadRight(80, '='), ConsoleColor.Cyan);
            LogMessage("THREAT INTELLIGENCE API VALIDATION", ConsoleColor.Cyan);
            LogMessage("=".PadRight(80, '='), ConsoleColor.Cyan);
            LogMessage("");

            report.Results["OTX"] = await TestOTXConnection();
            LogMessage("");

            report.Results["NVD"] = await TestNVDConnection();
            LogMessage("");

            report.Results["CISA"] = await TestCISAConnection();
            LogMessage("");

            report.NormalizationTest = await TestDataNormalization();
            LogMessage("");

            report.Summary = GenerateValidationReport(report);

            return report;
        }

        public async Task<ApiTestResult> TestOTXConnection()
        {
            var result = new ApiTestResult { ApiName = "AlienVault OTX", Status = "Unknown" };
            var stopwatch = Stopwatch.StartNew();

            LogMessage("Testing AlienVault OTX API Connection...", ConsoleColor.Yellow);

            try
            {
                using var httpClient = new HttpClient();
                var apiKey = "4454bd50246c8265987afd9f6c73cf99000e18e73c7abfe1f6a4364d815b2201";
                httpClient.DefaultRequestHeaders.Add("X-OTX-API-KEY", apiKey);

                var url = "https://otx.alienvault.com/api/v1/pulses/subscribed";
                var response = await httpClient.GetAsync(url);
                stopwatch.Stop();
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                {
                    result.Status = "Failed";
                    result.Error = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                    LogMessage($"❌ OTX Connection Failed: {result.Error}", ConsoleColor.Red);
                    return result;
                }

                var content = await response.Content.ReadAsStringAsync();
                var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                
                LogMessage($"✅ OTX API Connection Successful ({result.ResponseTimeMs}ms)", ConsoleColor.Green);
                LogMessage($"Raw JSON Response (first 500 chars):", ConsoleColor.Gray);
                LogMessage(preview, ConsoleColor.DarkGray);

                var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("results", out var results))
                {
                    var pulseCount = results.GetArrayLength();
                    result.ItemsCount = pulseCount;
                    result.Status = "Success";

                    LogMessage($"✅ Response contains 'results' array", ConsoleColor.Green);
                    LogMessage($"📊 Total Pulses Returned: {pulseCount}", ConsoleColor.Cyan);

                    if (pulseCount > 0 && results[0].ValueKind == JsonValueKind.Object)
                    {
                        var firstPulse = results[0];
                        
                        var name = firstPulse.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "N/A";
                        var description = firstPulse.TryGetProperty("description", out var descProp) ? descProp.GetString() : "N/A";
                        var created = firstPulse.TryGetProperty("created", out var createdProp) ? createdProp.GetString() : "N/A";
                        
                        var tags = new List<string>();
                        if (firstPulse.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tag in tagsProp.EnumerateArray())
                            {
                                tags.Add(tag.GetString() ?? "");
                            }
                        }

                        LogMessage($"\n📋 First Pulse Details:", ConsoleColor.Cyan);
                        LogMessage($"   Name: {name}", ConsoleColor.White);
                        LogMessage($"   Description: {(description?.Length > 100 ? description.Substring(0, 100) + "..." : description)}", ConsoleColor.White);
                        LogMessage($"   Created: {created}", ConsoleColor.White);
                        LogMessage($"   Tags: {string.Join(", ", tags)}", ConsoleColor.White);

                        result.SampleData = new
                        {
                            name,
                            description,
                            created,
                            tags
                        };

                        var bioMatches = 0;
                        var bioPulses = new List<object>();

                        foreach (var pulse in results.EnumerateArray())
                        {
                            var pulseText = pulse.GetRawText().ToLower();
                            var isBioRelevant = _bioEconomyKeywords.Any(keyword => pulseText.Contains(keyword.ToLower()));

                            if (isBioRelevant)
                            {
                                bioMatches++;
                                if (bioPulses.Count < 3)
                                {
                                    var pulseName = pulse.TryGetProperty("name", out var pn) ? pn.GetString() : "Unknown";
                                    bioPulses.Add(new { name = pulseName });
                                }
                            }
                        }

                        result.BioEconomyMatches = bioMatches;
                        LogMessage($"\n🔬 Bio-Economy Relevant Pulses: {bioMatches}", ConsoleColor.Magenta);
                        
                        if (bioPulses.Count > 0)
                        {
                            LogMessage("   Sample bio-economy pulses:", ConsoleColor.Magenta);
                            foreach (var bp in bioPulses)
                            {
                                LogMessage($"   - {bp}", ConsoleColor.DarkMagenta);
                            }
                        }
                    }
                }
                else
                {
                    result.Status = "Warning";
                    result.Warning = "Response does not contain 'results' array";
                    LogMessage($"⚠️  Warning: {result.Warning}", ConsoleColor.Yellow);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Status = "Failed";
                result.Error = ex.Message;
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                LogMessage($"❌ OTX Test Failed: {ex.Message}", ConsoleColor.Red);
            }

            return result;
        }

        public async Task<ApiTestResult> TestNVDConnection()
        {
            var result = new ApiTestResult { ApiName = "NIST NVD", Status = "Unknown" };
            var stopwatch = Stopwatch.StartNew();

            LogMessage("Testing NIST NVD API Connection...", ConsoleColor.Yellow);
            LogMessage("Step 1: Testing basic connectivity with minimal request...", ConsoleColor.Cyan);

            try
            {
                var connectivityTest = await _nistService.TestNVDConnection();
                stopwatch.Stop();
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

                if (!connectivityTest.Success)
                {
                    result.Status = "Failed";
                    result.Error = connectivityTest.Error ?? "NVD API connectivity test failed";
                    result.Warning = "NVD API may be temporarily down, endpoint changed, or URL structure incorrect. Check https://nvd.nist.gov/developers/vulnerabilities for status.";
                    LogMessage($"❌ NVD Basic Connectivity Test Failed: {result.Error}", ConsoleColor.Red);
                    LogMessage($"⚠️  {result.Warning}", ConsoleColor.Yellow);
                    return result;
                }

                LogMessage($"✅ NVD Basic Connectivity Test PASSED ({result.ResponseTimeMs}ms)", ConsoleColor.Green);
                
                if (connectivityTest.Data != null)
                {
                    var jsonString = JsonSerializer.Serialize(connectivityTest.Data);
                    var preview = jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString;
                    LogMessage($"Basic Test Response (first 500 chars):", ConsoleColor.Gray);
                    LogMessage(preview, ConsoleColor.DarkGray);
                }

                LogMessage("\nStep 2: Testing with keyword search (medical)...", ConsoleColor.Cyan);
                stopwatch.Restart();

                var keywordTest = await _nistService.FetchNVDCVEs("medical");
                stopwatch.Stop();
                result.ResponseTimeMs += stopwatch.ElapsedMilliseconds;

                if (keywordTest == null)
                {
                    result.Status = "Partial Success";
                    result.Warning = "Basic connectivity works but keyword search returned null";
                    LogMessage($"⚠️  {result.Warning}", ConsoleColor.Yellow);
                    return result;
                }

                result.Status = "Success";
                LogMessage($"✅ NVD Keyword Search Test PASSED", ConsoleColor.Green);

                var jsonString2 = JsonSerializer.Serialize(keywordTest);
                var preview2 = jsonString2.Length > 500 ? jsonString2.Substring(0, 500) + "..." : jsonString2;
                
                LogMessage($"Keyword Search Response (first 500 chars):", ConsoleColor.Gray);
                LogMessage(preview2, ConsoleColor.DarkGray);

                var jsonDoc = JsonDocument.Parse(jsonString2);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("vulnerabilities", out var vulnerabilities))
                {
                    var cveCount = vulnerabilities.GetArrayLength();
                    result.ItemsCount = cveCount;
                    
                    LogMessage($"✅ Response contains 'vulnerabilities' array", ConsoleColor.Green);
                    LogMessage($"📊 Total CVEs Returned: {cveCount}", ConsoleColor.Cyan);

                    if (cveCount > 0 && vulnerabilities[0].ValueKind == JsonValueKind.Object)
                    {
                        var firstCVE = vulnerabilities[0];
                        var cve = firstCVE.TryGetProperty("cve", out var cveProp) ? cveProp : default;

                        var cveId = "N/A";
                        var description = "N/A";
                        var published = "N/A";
                        var cvssScore = "N/A";

                        if (cve.ValueKind == JsonValueKind.Object)
                        {
                            if (cve.TryGetProperty("id", out var idProp))
                                cveId = idProp.GetString() ?? "N/A";

                            if (cve.TryGetProperty("descriptions", out var descsProp) && descsProp.ValueKind == JsonValueKind.Array && descsProp.GetArrayLength() > 0)
                            {
                                var firstDesc = descsProp[0];
                                if (firstDesc.TryGetProperty("value", out var descValue))
                                    description = descValue.GetString() ?? "N/A";
                            }

                            if (cve.TryGetProperty("published", out var pubProp))
                                published = pubProp.GetString() ?? "N/A";

                            if (cve.TryGetProperty("metrics", out var metricsProp))
                            {
                                if (metricsProp.TryGetProperty("cvssMetricV31", out var cvss31) && cvss31.ValueKind == JsonValueKind.Array && cvss31.GetArrayLength() > 0)
                                {
                                    var firstMetric = cvss31[0];
                                    if (firstMetric.TryGetProperty("cvssData", out var cvssData) && cvssData.TryGetProperty("baseScore", out var score))
                                        cvssScore = score.GetDouble().ToString("F1");
                                }
                                else if (metricsProp.TryGetProperty("cvssMetricV2", out var cvss2) && cvss2.ValueKind == JsonValueKind.Array && cvss2.GetArrayLength() > 0)
                                {
                                    var firstMetric = cvss2[0];
                                    if (firstMetric.TryGetProperty("cvssData", out var cvssData) && cvssData.TryGetProperty("baseScore", out var score))
                                        cvssScore = score.GetDouble().ToString("F1");
                                }
                            }
                        }

                        LogMessage($"\n📋 First CVE Details:", ConsoleColor.Cyan);
                        LogMessage($"   CVE ID: {cveId}", ConsoleColor.White);
                        LogMessage($"   Description: {(description.Length > 100 ? description.Substring(0, 100) + "..." : description)}", ConsoleColor.White);
                        LogMessage($"   CVSS Score: {cvssScore}", ConsoleColor.White);
                        LogMessage($"   Published: {published}", ConsoleColor.White);

                        result.SampleData = new
                        {
                            cveId,
                            description,
                            cvssScore,
                            published,
                            testFormat = "Both basic and keyword search formats work"
                        };
                    }
                }
                else
                {
                    result.Status = "Warning";
                    result.Warning = "Response does not contain 'vulnerabilities' array";
                    LogMessage($"⚠️  Warning: {result.Warning}", ConsoleColor.Yellow);
                    LogMessage($"Response structure: {root}", ConsoleColor.DarkGray);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Status = "Failed";
                result.Error = ex.Message;
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                result.Warning = "NVD API may be temporarily unavailable. OTX and CISA tests will continue.";
                LogMessage($"❌ NVD Test Failed: {ex.Message}", ConsoleColor.Red);
                LogMessage($"⚠️  {result.Warning}", ConsoleColor.Yellow);
                LogMessage($"   Stack trace: {ex.StackTrace}", ConsoleColor.DarkRed);
            }

            return result;
        }

        public async Task<ApiTestResult> TestCISAConnection()
        {
            var result = new ApiTestResult { ApiName = "CISA KEV", Status = "Unknown" };
            var stopwatch = Stopwatch.StartNew();

            LogMessage("Testing CISA Known Exploited Vulnerabilities Feed...", ConsoleColor.Yellow);

            try
            {
                var url = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";
                
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);
                stopwatch.Stop();
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                {
                    result.Status = "Failed";
                    result.Error = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                    LogMessage($"❌ CISA Connection Failed: {result.Error}", ConsoleColor.Red);
                    return result;
                }

                result.Status = "Success";
                LogMessage($"✅ CISA Feed Connection Successful ({result.ResponseTimeMs}ms)", ConsoleColor.Green);

                var content = await response.Content.ReadAsStringAsync();
                var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                
                LogMessage($"Raw JSON Response (first 500 chars):", ConsoleColor.Gray);
                LogMessage(preview, ConsoleColor.DarkGray);

                var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("vulnerabilities", out var vulnerabilities))
                {
                    var vulnCount = vulnerabilities.GetArrayLength();
                    result.ItemsCount = vulnCount;
                    
                    LogMessage($"✅ Response contains 'vulnerabilities' array", ConsoleColor.Green);
                    LogMessage($"📊 Total Vulnerabilities in Catalog: {vulnCount}", ConsoleColor.Cyan);

                    if (vulnCount > 0 && vulnerabilities[0].ValueKind == JsonValueKind.Object)
                    {
                        var firstVuln = vulnerabilities[0];
                        
                        var cveId = firstVuln.TryGetProperty("cveID", out var cveProp) ? cveProp.GetString() : "N/A";
                        var vendor = firstVuln.TryGetProperty("vendorProject", out var vendorProp) ? vendorProp.GetString() : "N/A";
                        var product = firstVuln.TryGetProperty("product", out var productProp) ? productProp.GetString() : "N/A";
                        var vulnName = firstVuln.TryGetProperty("vulnerabilityName", out var nameProp) ? nameProp.GetString() : "N/A";
                        var dateAdded = firstVuln.TryGetProperty("dateAdded", out var dateProp) ? dateProp.GetString() : "N/A";
                        var ransomwareUse = firstVuln.TryGetProperty("knownRansomwareCampaignUse", out var ransomProp) ? ransomProp.GetString() : "N/A";

                        LogMessage($"\n📋 First Vulnerability Details:", ConsoleColor.Cyan);
                        LogMessage($"   CVE ID: {cveId}", ConsoleColor.White);
                        LogMessage($"   Vendor: {vendor}", ConsoleColor.White);
                        LogMessage($"   Product: {product}", ConsoleColor.White);
                        LogMessage($"   Vulnerability Name: {vulnName}", ConsoleColor.White);
                        LogMessage($"   Date Added: {dateAdded}", ConsoleColor.White);
                        LogMessage($"   Ransomware Use: {ransomwareUse}", ConsoleColor.White);

                        result.SampleData = new
                        {
                            cveId,
                            vendor,
                            product,
                            vulnerabilityName = vulnName,
                            dateAdded,
                            knownRansomwareCampaignUse = ransomwareUse
                        };

                        var bioMatches = 0;
                        var bioVulns = new List<object>();

                        foreach (var vuln in vulnerabilities.EnumerateArray())
                        {
                            var vulnText = vuln.GetRawText().ToLower();
                            var isBioRelevant = _bioEconomyKeywords.Any(keyword => vulnText.Contains(keyword.ToLower()));

                            if (isBioRelevant)
                            {
                                bioMatches++;
                                if (bioVulns.Count < 3)
                                {
                                    var vCveId = vuln.TryGetProperty("cveID", out var vc) ? vc.GetString() : "Unknown";
                                    var vProduct = vuln.TryGetProperty("product", out var vp) ? vp.GetString() : "Unknown";
                                    bioVulns.Add(new { cveId = vCveId, product = vProduct });
                                }
                            }
                        }

                        result.BioEconomyMatches = bioMatches;
                        LogMessage($"\n🔬 Bio-Economy Relevant Vulnerabilities: {bioMatches}", ConsoleColor.Magenta);
                        
                        if (bioVulns.Count > 0)
                        {
                            LogMessage("   Sample bio-economy vulnerabilities:", ConsoleColor.Magenta);
                            foreach (var bv in bioVulns)
                            {
                                LogMessage($"   - {bv}", ConsoleColor.DarkMagenta);
                            }
                        }
                    }
                }
                else
                {
                    result.Status = "Warning";
                    result.Warning = "Response does not contain 'vulnerabilities' array";
                    LogMessage($"⚠️  Warning: {result.Warning}", ConsoleColor.Yellow);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Status = "Failed";
                result.Error = ex.Message;
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                LogMessage($"❌ CISA Test Failed: {ex.Message}", ConsoleColor.Red);
            }

            return result;
        }

        public async Task<NormalizationTestResult> TestDataNormalization()
        {
            var result = new NormalizationTestResult { Status = "Unknown" };
            
            LogMessage("Testing Data Normalization to Threat Objects...", ConsoleColor.Yellow);

            try
            {
                var threats = new List<api.Models.Threat>();

                using var httpClient = new HttpClient();
                var apiKey = "4454bd50246c8265987afd9f6c73cf99000e18e73c7abfe1f6a4364d815b2201";
                httpClient.DefaultRequestHeaders.Add("X-OTX-API-KEY", apiKey);

                var otxUrl = "https://otx.alienvault.com/api/v1/pulses/subscribed";
                var otxResponse = await httpClient.GetAsync(otxUrl);
                if (otxResponse.IsSuccessStatusCode)
                {
                    var otxContent = await otxResponse.Content.ReadAsStringAsync();
                    var otxDoc = JsonDocument.Parse(otxContent);
                    if (otxDoc.RootElement.TryGetProperty("results", out var otxResults) && otxResults.GetArrayLength() > 0)
                    {
                        var pulse = otxResults[0];
                        var threat = new api.Models.Threat
                        {
                            title = pulse.TryGetProperty("name", out var n) ? n.GetString() ?? "OTX Pulse" : "OTX Pulse",
                            description = pulse.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                            category = "Malware",
                            source = "OTX",
                            date_observed = pulse.TryGetProperty("created", out var c) && DateTime.TryParse(c.GetString(), out var dt) ? dt : DateTime.UtcNow,
                            impact_level = "Medium",
                            external_reference = pulse.TryGetProperty("id", out var id) ? $"https://otx.alienvault.com/pulse/{id.GetString()}" : null,
                            status = "New",
                            user_id = null
                        };
                        threats.Add(threat);
                        result.OTXThreat = threat;
                    }
                }

                var nvdUrl = "https://services.nvd.nist.gov/rest/json/cves/2.0?resultsPerPage=1&apiKey=87ddcef2-a244-420d-baf8-1a9223b67e67";
                var nvdResponse = await httpClient.GetAsync(nvdUrl);
                if (nvdResponse.IsSuccessStatusCode)
                {
                    var nvdContent = await nvdResponse.Content.ReadAsStringAsync();
                    var nvdDoc = JsonDocument.Parse(nvdContent);
                    if (nvdDoc.RootElement.TryGetProperty("vulnerabilities", out var nvdVulns) && nvdVulns.GetArrayLength() > 0)
                    {
                        var cve = nvdVulns[0].GetProperty("cve");
                        var cveId = cve.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                        var description = "";
                        if (cve.TryGetProperty("descriptions", out var descs) && descs.GetArrayLength() > 0)
                        {
                            description = descs[0].TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";
                        }
                        var published = cve.TryGetProperty("published", out var pub) && DateTime.TryParse(pub.GetString(), out var pubDt) ? pubDt : DateTime.UtcNow;

                        var threat = new api.Models.Threat
                        {
                            title = cveId,
                            description = description,
                            category = "Vulnerability",
                            source = "NVD",
                            date_observed = published,
                            impact_level = "High",
                            external_reference = $"https://nvd.nist.gov/vuln/detail/{cveId}",
                            status = "New",
                            user_id = null
                        };
                        threats.Add(threat);
                        result.NVDThreat = threat;
                    }
                }

                var cisaUrl = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";
                var cisaResponse = await httpClient.GetAsync(cisaUrl);
                if (cisaResponse.IsSuccessStatusCode)
                {
                    var cisaContent = await cisaResponse.Content.ReadAsStringAsync();
                    var cisaDoc = JsonDocument.Parse(cisaContent);
                    if (cisaDoc.RootElement.TryGetProperty("vulnerabilities", out var cisaVulns) && cisaVulns.GetArrayLength() > 0)
                    {
                        var vuln = cisaVulns[0];
                        var cveId = vuln.TryGetProperty("cveID", out var cid) ? cid.GetString() ?? "" : "";
                        var vulnName = vuln.TryGetProperty("vulnerabilityName", out var vn) ? vn.GetString() ?? "" : "";
                        var shortDesc = vuln.TryGetProperty("shortDescription", out var sd) ? sd.GetString() ?? "" : "";
                        var dateAdded = vuln.TryGetProperty("dateAdded", out var da) && DateTime.TryParse(da.GetString(), out var daDt) ? daDt : DateTime.UtcNow;

                        var threat = new api.Models.Threat
                        {
                            title = vulnName,
                            description = shortDesc,
                            category = "Exploited Vulnerability",
                            source = "CISA",
                            date_observed = dateAdded,
                            impact_level = "Critical",
                            external_reference = $"https://nvd.nist.gov/vuln/detail/{cveId}",
                            status = "New",
                            user_id = null
                        };
                        threats.Add(threat);
                        result.CISAThreat = threat;
                    }
                }

                result.Status = "Success";
                result.Threats = threats;
                result.AllFieldsPopulated = threats.All(t => 
                    !string.IsNullOrEmpty(t.title) &&
                    !string.IsNullOrEmpty(t.description) &&
                    !string.IsNullOrEmpty(t.category) &&
                    !string.IsNullOrEmpty(t.source) &&
                    !string.IsNullOrEmpty(t.impact_level) &&
                    !string.IsNullOrEmpty(t.status));

                LogMessage($"✅ Data Normalization Test Complete", ConsoleColor.Green);
                LogMessage($"\n📋 Normalized Threat Objects:", ConsoleColor.Cyan);
                
                foreach (var threat in threats)
                {
                    LogMessage($"\n   Source: {threat.source}", ConsoleColor.White);
                    LogMessage($"   Title: {threat.title}", ConsoleColor.White);
                    LogMessage($"   Category: {threat.category}", ConsoleColor.White);
                    LogMessage($"   Impact: {threat.impact_level}", ConsoleColor.White);
                    LogMessage($"   Date: {threat.date_observed:yyyy-MM-dd}", ConsoleColor.White);
                    LogMessage($"   Reference: {threat.external_reference ?? "N/A"}", ConsoleColor.White);
                }

                LogMessage($"\n✅ All Required Fields Populated: {result.AllFieldsPopulated}", 
                    result.AllFieldsPopulated ? ConsoleColor.Green : ConsoleColor.Red);
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Error = ex.Message;
                LogMessage($"❌ Normalization Test Failed: {ex.Message}", ConsoleColor.Red);
            }

            return result;
        }

        public ValidationSummary GenerateValidationReport(ValidationReport report)
        {
            var summary = new ValidationSummary
            {
                TotalAPIs = report.Results.Count,
                SuccessfulAPIs = report.Results.Values.Count(r => r.Status == "Success" || r.Status == "Success (No API Key)"),
                FailedAPIs = report.Results.Values.Count(r => r.Status == "Failed"),
                Warnings = report.Results.Values.Where(r => !string.IsNullOrEmpty(r.Warning)).Select(r => r.Warning!).ToList(),
                Errors = report.Results.Values.Where(r => !string.IsNullOrEmpty(r.Error)).Select(r => r.Error!).ToList(),
                Recommendations = new List<string>()
            };

            LogMessage("=".PadRight(80, '='), ConsoleColor.Cyan);
            LogMessage("VALIDATION SUMMARY REPORT", ConsoleColor.Cyan);
            LogMessage("=".PadRight(80, '='), ConsoleColor.Cyan);
            LogMessage("");

            foreach (var kvp in report.Results)
            {
                var apiName = kvp.Key;
                var apiResult = kvp.Value;
                var statusIcon = apiResult.Status == "Success" || apiResult.Status == "Success (No API Key)" ? "✅" : "❌";
                
                LogMessage($"{statusIcon} {apiName}: {apiResult.Status}", 
                    apiResult.Status.Contains("Success") ? ConsoleColor.Green : ConsoleColor.Red);
                LogMessage($"   Items Fetched: {apiResult.ItemsCount}", ConsoleColor.Cyan);
                LogMessage($"   Response Time: {apiResult.ResponseTimeMs}ms", ConsoleColor.Cyan);
                if (apiResult.BioEconomyMatches > 0)
                {
                    LogMessage($"   Bio-Economy Matches: {apiResult.BioEconomyMatches}", ConsoleColor.Magenta);
                }
                if (!string.IsNullOrEmpty(apiResult.Warning))
                {
                    LogMessage($"   ⚠️  Warning: {apiResult.Warning}", ConsoleColor.Yellow);
                }
                if (!string.IsNullOrEmpty(apiResult.Error))
                {
                    LogMessage($"   ❌ Error: {apiResult.Error}", ConsoleColor.Red);
                }
                LogMessage("");
            }

            if (report.NormalizationTest != null)
            {
                LogMessage($"📊 Data Normalization: {report.NormalizationTest.Status}", 
                    report.NormalizationTest.Status == "Success" ? ConsoleColor.Green : ConsoleColor.Red);
                LogMessage($"   Threats Normalized: {report.NormalizationTest.Threats?.Count ?? 0}", ConsoleColor.Cyan);
                LogMessage($"   All Fields Populated: {report.NormalizationTest.AllFieldsPopulated}", 
                    report.NormalizationTest.AllFieldsPopulated ? ConsoleColor.Green : ConsoleColor.Red);
                LogMessage("");
            }

            if (summary.Warnings.Count > 0)
            {
                LogMessage("⚠️  WARNINGS:", ConsoleColor.Yellow);
                foreach (var warning in summary.Warnings)
                {
                    LogMessage($"   - {warning}", ConsoleColor.Yellow);
                }
                LogMessage("");
            }

            if (summary.Errors.Count > 0)
            {
                LogMessage("❌ ERRORS:", ConsoleColor.Red);
                foreach (var error in summary.Errors)
                {
                    LogMessage($"   - {error}", ConsoleColor.Red);
                }
                LogMessage("");
            }

            if (report.Results["NVD"]?.Status.Contains("No API Key") == true)
            {
                summary.Recommendations.Add("Consider using NVD API key for higher rate limits (50 requests per 30 seconds vs 5 requests per 30 seconds)");
            }

            if (report.Results.Values.Any(r => r.ResponseTimeMs > 5000))
            {
                summary.Recommendations.Add("Some API calls are slow. Consider implementing caching or async processing.");
            }

            if (summary.Recommendations.Count > 0)
            {
                LogMessage("💡 RECOMMENDATIONS:", ConsoleColor.Cyan);
                foreach (var rec in summary.Recommendations)
                {
                    LogMessage($"   - {rec}", ConsoleColor.Cyan);
                }
                LogMessage("");
            }

            LogMessage("=".PadRight(80, '='), ConsoleColor.Cyan);

            return summary;
        }

        private void LogMessage(string message, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            _logger.LogInformation(message);
        }
    }

    public class ValidationReport
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, ApiTestResult> Results { get; set; } = new();
        public NormalizationTestResult? NormalizationTest { get; set; }
        public ValidationSummary Summary { get; set; } = new();
    }

    public class ApiTestResult
    {
        public string ApiName { get; set; } = "";
        public string Status { get; set; } = "";
        public int ItemsCount { get; set; }
        public long ResponseTimeMs { get; set; }
        public int BioEconomyMatches { get; set; }
        public string? Error { get; set; }
        public string? Warning { get; set; }
        public object? SampleData { get; set; }
    }

    public class NormalizationTestResult
    {
        public string Status { get; set; } = "";
        public List<api.Models.Threat> Threats { get; set; } = new();
        public api.Models.Threat? OTXThreat { get; set; }
        public api.Models.Threat? NVDThreat { get; set; }
        public api.Models.Threat? CISAThreat { get; set; }
        public bool AllFieldsPopulated { get; set; }
        public string? Error { get; set; }
    }

    public class ValidationSummary
    {
        public int TotalAPIs { get; set; }
        public int SuccessfulAPIs { get; set; }
        public int FailedAPIs { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }
}

