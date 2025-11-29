using System.Text.Json;
using api.Models;
using MyApp.Namespace.Services;

namespace MyApp.Namespace.Services
{
    public class ThreatNormalizationService
    {
        private readonly string[] _bioEconomyKeywords = new[]
        {
            "medical", "healthcare", "hospital", "lab", "laboratory", "pharmaceutical",
            "pharma", "scada", "ics", "biotech", "biotechnology", "clinical", "patient",
            "diagnostic", "therapeutic", "biomedical", "health", "care", "device"
        };

        public List<Threat> NormalizeOTXThreats(object? otxData)
        {
            var threats = new List<Threat>();

            if (otxData == null) return threats;

            try
            {
                var jsonString = JsonSerializer.Serialize(otxData);
                var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pulse in results.EnumerateArray())
                    {
                        var threat = NormalizeOTXPulse(pulse);
                        if (threat != null && IsBioEconomyRelevant(threat))
                        {
                            threats.Add(threat);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to normalize OTX threats: {ex.Message}", ex);
            }

            return threats;
        }

        private Threat? NormalizeOTXPulse(JsonElement pulse)
        {
            try
            {
                var name = pulse.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var description = pulse.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var created = pulse.TryGetProperty("created", out var c) && DateTime.TryParse(c.GetString(), out var dt) ? dt : DateTime.UtcNow;
                var pulseId = pulse.TryGetProperty("id", out var id) ? id.GetString() : null;

                if (string.IsNullOrEmpty(name)) return null;

                return new Threat
                {
                    title = name,
                    description = description,
                    category = "Malware",
                    source = "OTX",
                    date_observed = created,
                    impact_level = "Medium",
                    external_reference = pulseId != null ? $"https://otx.alienvault.com/pulse/{pulseId}" : null,
                    status = "Pending_AI",
                    user_id = 1
                };
            }
            catch
            {
                return null;
            }
        }

        public List<Threat> NormalizeNVDThreats(object? nvdData)
        {
            var threats = new List<Threat>();

            if (nvdData == null) return threats;

            try
            {
                var jsonString = JsonSerializer.Serialize(nvdData);
                var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("vulnerabilities", out var vulnerabilities) && vulnerabilities.ValueKind == JsonValueKind.Array)
                {
                    foreach (var vuln in vulnerabilities.EnumerateArray())
                    {
                        var threat = NormalizeNVDCVE(vuln);
                        if (threat != null && IsBioEconomyRelevant(threat))
                        {
                            threats.Add(threat);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to normalize NVD threats: {ex.Message}", ex);
            }

            return threats;
        }

        private Threat? NormalizeNVDCVE(JsonElement vuln)
        {
            try
            {
                if (!vuln.TryGetProperty("cve", out var cve)) return null;

                var cveId = cve.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(cveId)) return null;

                var description = "";
                if (cve.TryGetProperty("descriptions", out var descs) && descs.ValueKind == JsonValueKind.Array && descs.GetArrayLength() > 0)
                {
                    var firstDesc = descs[0];
                    if (firstDesc.TryGetProperty("value", out var descValue))
                        description = descValue.GetString() ?? "";
                }

                var published = cve.TryGetProperty("published", out var pub) && DateTime.TryParse(pub.GetString(), out var pubDt) ? pubDt : DateTime.UtcNow;

                var impactLevel = "High";
                if (cve.TryGetProperty("metrics", out var metrics))
                {
                    if (metrics.TryGetProperty("cvssMetricV31", out var cvss31) && cvss31.ValueKind == JsonValueKind.Array && cvss31.GetArrayLength() > 0)
                    {
                        var firstMetric = cvss31[0];
                        if (firstMetric.TryGetProperty("cvssData", out var cvssData) && cvssData.TryGetProperty("baseScore", out var score))
                        {
                            var baseScore = score.GetDouble();
                            impactLevel = baseScore >= 9.0 ? "Critical" : baseScore >= 7.0 ? "High" : baseScore >= 4.0 ? "Medium" : "Low";
                        }
                    }
                    else if (metrics.TryGetProperty("cvssMetricV2", out var cvss2) && cvss2.ValueKind == JsonValueKind.Array && cvss2.GetArrayLength() > 0)
                    {
                        var firstMetric = cvss2[0];
                        if (firstMetric.TryGetProperty("cvssData", out var cvssData) && cvssData.TryGetProperty("baseScore", out var score))
                        {
                            var baseScore = score.GetDouble();
                            impactLevel = baseScore >= 9.0 ? "Critical" : baseScore >= 7.0 ? "High" : baseScore >= 4.0 ? "Medium" : "Low";
                        }
                    }
                }

                return new Threat
                {
                    title = cveId,
                    description = description,
                    category = "Vulnerability",
                    source = "NVD",
                    date_observed = published,
                    impact_level = impactLevel,
                    external_reference = $"https://nvd.nist.gov/vuln/detail/{cveId}",
                    status = "Pending_AI",
                    user_id = 1
                };
            }
            catch
            {
                return null;
            }
        }

        public List<Threat> NormalizeCISAThreats(object? cisaData)
        {
            var threats = new List<Threat>();

            if (cisaData == null) return threats;

            try
            {
                var jsonString = JsonSerializer.Serialize(cisaData);
                var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("vulnerabilities", out var vulnerabilities) && vulnerabilities.ValueKind == JsonValueKind.Array)
                {
                    foreach (var vuln in vulnerabilities.EnumerateArray())
                    {
                        var threat = NormalizeCISAVulnerability(vuln);
                        if (threat != null && IsBioEconomyRelevant(threat))
                        {
                            threats.Add(threat);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to normalize CISA threats: {ex.Message}", ex);
            }

            return threats;
        }

        private Threat? NormalizeCISAVulnerability(JsonElement vuln)
        {
            try
            {
                var cveId = vuln.TryGetProperty("cveID", out var cid) ? cid.GetString() ?? "" : "";
                var vulnName = vuln.TryGetProperty("vulnerabilityName", out var vn) ? vn.GetString() ?? "" : "";
                var shortDesc = vuln.TryGetProperty("shortDescription", out var sd) ? sd.GetString() ?? "" : "";
                var dateAdded = vuln.TryGetProperty("dateAdded", out var da) && DateTime.TryParse(da.GetString(), out var daDt) ? daDt : DateTime.UtcNow;
                var ransomwareUse = vuln.TryGetProperty("knownRansomwareCampaignUse", out var ru) ? ru.GetString() ?? "Unknown" : "Unknown";

                if (string.IsNullOrEmpty(cveId) && string.IsNullOrEmpty(vulnName)) return null;

                var impactLevel = ransomwareUse.Equals("Known", StringComparison.OrdinalIgnoreCase) ? "Critical" : "High";

                return new Threat
                {
                    title = !string.IsNullOrEmpty(vulnName) ? vulnName : cveId,
                    description = shortDesc,
                    category = "Exploited Vulnerability",
                    source = "CISA",
                    date_observed = dateAdded,
                    impact_level = impactLevel,
                    external_reference = !string.IsNullOrEmpty(cveId) ? $"https://nvd.nist.gov/vuln/detail/{cveId}" : null,
                    status = "Pending_AI",
                    user_id = 1
                };
            }
            catch
            {
                return null;
            }
        }

        private bool IsBioEconomyRelevant(Threat threat)
        {
            var searchText = $"{threat.title} {threat.description} {threat.category}".ToLower();
            return _bioEconomyKeywords.Any(keyword => searchText.Contains(keyword.ToLower()));
        }
    }
}

