# AI Threat Rating Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Automatically rate all ingested threats using Gemini AI with Google Search Grounding for fact-checking.

**Architecture:** Enhance existing AIService with Search Grounding, add background timer to process unrated threats in configurable batches with rate limiting, expose API endpoints for manual control.

**Tech Stack:** C# .NET 9, Gemini 1.5 Pro API, MySQL, Background Services

---

## Task 1: Enhance AIService with Google Search Grounding

**Files:**
- Modify: `api/Services/AIService.cs:48-71` (requestBody construction)
- Modify: `api/Services/AIService.cs:120-179` (BuildClassificationPrompt method)

**Step 1: Add Google Search Grounding to Gemini API request**

In `AIService.cs`, modify the `requestBody` in `ClassifyThreatAsync` method:

```csharp
// Around line 48-71
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
    },
    tools = new[]  // ADD THIS
    {
        new { google_search_retrieval = new { } }
    }
};
```

**Step 2: Update prompt to utilize search grounding**

In `BuildClassificationPrompt` method (line 120), add instructions after the threat details section:

```csharp
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

VERIFICATION INSTRUCTIONS:
Use web search to verify if this threat has been reported by reputable sources (security vendors, CERT teams, government advisories).
Adjust your confidence score based on:
- Multiple independent sources confirming the threat: Higher confidence (80-100%)
- Single source or unverified claims: Medium confidence (50-79%)
- No corroborating sources found or conflicting information: Lower confidence (0-49%)
- Include source references in your reasoning when available

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

Respond ONLY with valid JSON in this exact format:
{{
    ""tier"": ""High"" | ""Medium"" | ""Low"",
    ""confidence"": 0-100,
    ""reasoning"": ""Detailed explanation of classification including source verification"",
    ""recommendedActions"": ""List of recommended actions"",
    ""keywords"": [""keyword1"", ""keyword2""],
    ""bioSectorRelevance"": 0-100
}}";
}
```

**Step 3: Test manually with existing classification**

Run the API and trigger a classification on an existing threat to verify Search Grounding works:

```bash
dotnet run --project api/api.csproj
# In another terminal, test with curl or Postman
# POST to existing classification endpoint
```

Expected: API response includes references to verified sources in reasoning field.

**Step 4: Commit**

```bash
git add api/Services/AIService.cs
git commit -m "feat: add Google Search Grounding to threat classification

Enhanced Gemini API request with google_search_retrieval tool.
Updated prompt to instruct AI on source verification.
Confidence scores now reflect fact-checked validity.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Task 2: Add Background AI Rating Timer Infrastructure

**Files:**
- Modify: `api/Services/ThreatIngestionBackgroundService.cs:17-18` (add timer field)
- Modify: `api/Services/ThreatIngestionBackgroundService.cs:31-80` (ExecuteAsync method)
- Modify: `api/Services/ThreatIngestionBackgroundService.cs:250-267` (Dispose methods)

**Step 1: Add timer field**

At the top of the class (around line 17), add:

```csharp
private Timer? _otxTimer;
private Timer? _nvdTimer;
private Timer? _cisaTimer;
private Timer? _aiRatingTimer;  // ADD THIS
private bool _isRunning = false;
```

**Step 2: Initialize timer in ExecuteAsync**

In `ExecuteAsync` method (around line 74), after the CISA timer initialization:

```csharp
if (cisaEnabled)
{
    _cisaTimer = new Timer(async _ => await SyncCISAAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(cisaInterval));
    _logger.LogInformation($"CISA sync scheduled every {cisaInterval} minutes");
}

// ADD THIS BLOCK
var aiRatingEnabled = _configuration.GetValue<bool>("ThreatIngestion:AIRating:Enabled", true);
var aiRatingInterval = _configuration.GetValue<int>("ThreatIngestion:AIRating:IntervalMinutes", 15);

if (aiRatingEnabled)
{
    // Start after 1 minute delay to let initial ingestion complete
    _aiRatingTimer = new Timer(
        async _ => await ProcessAIRatingsAsync(),
        null,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(aiRatingInterval)
    );
    _logger.LogInformation($"AI rating scheduled every {aiRatingInterval} minutes");
}
```

**Step 3: Update Dispose methods**

In `Stop()` method (around line 250):

```csharp
public void Stop()
{
    _isRunning = false;
    _otxTimer?.Dispose();
    _nvdTimer?.Dispose();
    _cisaTimer?.Dispose();
    _aiRatingTimer?.Dispose();  // ADD THIS
    _cancellationTokenSource.Cancel();
    _logger.LogInformation("Threat Ingestion Background Service stopped");
}
```

In `Dispose()` method (around line 260):

```csharp
public override void Dispose()
{
    _otxTimer?.Dispose();
    _nvdTimer?.Dispose();
    _cisaTimer?.Dispose();
    _aiRatingTimer?.Dispose();  // ADD THIS
    _cancellationTokenSource.Dispose();
    base.Dispose();
}
```

**Step 4: Commit**

```bash
git add api/Services/ThreatIngestionBackgroundService.cs
git commit -m "feat: add AI rating timer infrastructure

Added timer field and initialization for periodic AI rating.
Configured with 15-minute default interval.
Starts after 1-minute delay to allow initial ingestion.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Task 3: Implement Background AI Rating with Rate Limiting

**Files:**
- Modify: `api/Services/ThreatIngestionBackgroundService.cs:236` (add new method before SyncAllAsync)

**Step 1: Add ProcessAIRatingsAsync method**

Before the `SyncAllAsync` method (around line 238), add this complete method:

```csharp
public async Task ProcessAIRatingsAsync()
{
    using var scope = _serviceProvider.CreateScope();
    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    var aiService = scope.ServiceProvider.GetRequiredService<AIService>();
    var auditLogService = scope.ServiceProvider.GetRequiredService<AuditLogService>();

    _logger.LogInformation("Starting AI rating batch processing...");

    try
    {
        await auditLogService.LogAsync("ai_rating_started", "Starting AI rating batch");

        // Get configuration
        var batchSize = _configuration.GetValue<int>("ThreatIngestion:AIRating:BatchSize", 10);
        var delaySeconds = _configuration.GetValue<int>("ThreatIngestion:AIRating:DelayBetweenRequestsSeconds", 4);
        var maxRetries = _configuration.GetValue<int>("ThreatIngestion:AIRating:MaxRetries", 3);
        var retryDelaySeconds = _configuration.GetValue<int>("ThreatIngestion:AIRating:RetryDelaySeconds", 30);

        // Query for unrated threats
        using var connection = await dbService.GetConnectionAsync();
        var query = @"
            SELECT t.* FROM threats t
            LEFT JOIN classifications c ON t.id = c.threat_id
            WHERE c.id IS NULL
            LIMIT @batchSize";

        using var command = new MySqlConnector.MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@batchSize", batchSize);

        var unratedThreats = new List<api.Models.Threat>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            unratedThreats.Add(new api.Models.Threat
            {
                id = reader.GetInt32("id"),
                title = reader.GetString("title"),
                description = reader.GetString("description"),
                category = reader.GetString("category"),
                source = reader.GetString("source"),
                date_observed = reader.GetDateTime("date_observed"),
                impact_level = reader.GetString("impact_level"),
                external_reference = reader.IsDBNull(reader.GetOrdinal("external_reference"))
                    ? null
                    : reader.GetString("external_reference")
            });
        }

        if (unratedThreats.Count == 0)
        {
            _logger.LogInformation("No unrated threats found");
            return;
        }

        _logger.LogInformation($"Processing {unratedThreats.Count} unrated threats");

        int successCount = 0;
        int failureCount = 0;

        // Process each threat with rate limiting and retries
        foreach (var threat in unratedThreats)
        {
            var retryCount = 0;
            var currentRetryDelay = retryDelaySeconds;
            var rated = false;

            while (retryCount <= maxRetries && !rated)
            {
                try
                {
                    _logger.LogInformation($"Rating threat {threat.id}: {threat.title}");

                    var classification = await aiService.ClassifyThreatAsync(threat);
                    await aiService.SaveClassificationAsync(threat.id!.Value, classification);

                    successCount++;
                    rated = true;

                    _logger.LogInformation($"Successfully rated threat {threat.id}");

                    // Delay between requests for rate limiting
                    if (unratedThreats.IndexOf(threat) < unratedThreats.Count - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    }
                }
                catch (Exception ex)
                {
                    retryCount++;

                    // Check if it's a rate limit error (429 or specific message)
                    var isRateLimit = ex.Message.Contains("429") ||
                                     ex.Message.Contains("rate limit") ||
                                     ex.Message.Contains("quota");

                    if (retryCount <= maxRetries && isRateLimit)
                    {
                        _logger.LogWarning(
                            $"Rate limit hit for threat {threat.id}, retry {retryCount}/{maxRetries} " +
                            $"after {currentRetryDelay}s: {ex.Message}"
                        );

                        await Task.Delay(TimeSpan.FromSeconds(currentRetryDelay));
                        currentRetryDelay *= 2; // Exponential backoff
                    }
                    else if (retryCount > maxRetries)
                    {
                        _logger.LogError(
                            ex,
                            $"Failed to rate threat {threat.id} after {maxRetries} retries, saving default classification"
                        );

                        // Save default classification as per design
                        var defaultClassification = new ClassificationResult
                        {
                            Tier = ThreatTier.Medium,
                            Confidence = 0,
                            Reasoning = $"AI rating failed after {maxRetries} retries: {ex.Message}. Manual review required.",
                            RecommendedActions = "Review threat manually and assign appropriate tier.",
                            Keywords = new List<string>(),
                            BioSectorRelevance = 50,
                            RawResponse = $"Error: {ex.Message}"
                        };

                        await aiService.SaveClassificationAsync(threat.id!.Value, defaultClassification);
                        failureCount++;
                        rated = true; // Mark as processed even though it failed
                    }
                    else
                    {
                        // Non-rate-limit error, fail immediately
                        _logger.LogError(ex, $"Error rating threat {threat.id}: {ex.Message}");

                        var defaultClassification = new ClassificationResult
                        {
                            Tier = ThreatTier.Medium,
                            Confidence = 0,
                            Reasoning = $"AI rating error: {ex.Message}. Manual review required.",
                            RecommendedActions = "Review threat manually and assign appropriate tier.",
                            Keywords = new List<string>(),
                            BioSectorRelevance = 50,
                            RawResponse = $"Error: {ex.Message}"
                        };

                        await aiService.SaveClassificationAsync(threat.id!.Value, defaultClassification);
                        failureCount++;
                        rated = true;
                    }
                }
            }
        }

        var message = $"AI Rating: Processed {unratedThreats.Count} threats, " +
                     $"{successCount} successful, {failureCount} failed (saved defaults)";

        await auditLogService.LogAsync("ai_rating_completed", message);
        _logger.LogInformation($"✅ {message}");
    }
    catch (Exception ex)
    {
        var errorMsg = $"AI rating batch processing failed: {ex.Message}";
        _logger.LogError(ex, $"❌ {errorMsg}");
        await auditLogService.LogAsync("ai_rating_failed", errorMsg);
    }
}
```

**Step 2: Build to verify no syntax errors**

```bash
dotnet build api/api.csproj
```

Expected: Build succeeds with no new errors.

**Step 3: Commit**

```bash
git add api/Services/ThreatIngestionBackgroundService.cs
git commit -m "feat: implement AI rating with rate limiting and retries

Added ProcessAIRatingsAsync method that:
- Queries for unrated threats in configurable batches
- Implements exponential backoff on rate limit errors
- Retries up to 3 times with increasing delays
- Saves default classification on failures
- Logs all rating activities for audit trail

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Task 4: Create ThreatRatingController

**Files:**
- Create: `api/Controllers/ThreatRatingController.cs`

**Step 1: Create ThreatRatingController with all three endpoints**

Create new file `api/Controllers/ThreatRatingController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;
using api.Services;
using api.Models;

namespace api.Controllers
{
    [ApiController]
    [Route("api/threats")]
    public class ThreatRatingController : ControllerBase
    {
        private readonly DatabaseService _dbService;
        private readonly AIService _aiService;
        private readonly ILogger<ThreatRatingController> _logger;

        public ThreatRatingController(
            DatabaseService dbService,
            AIService aiService,
            ILogger<ThreatRatingController> logger)
        {
            _dbService = dbService;
            _aiService = aiService;
            _logger = logger;
        }

        /// <summary>
        /// Get AI rating/classification for a specific threat
        /// </summary>
        [HttpGet("{id}/ai-rating")]
        public async Task<IActionResult> GetAIRating(int id)
        {
            try
            {
                using var connection = await _dbService.GetConnectionAsync();
                var query = @"
                    SELECT c.* FROM classifications c
                    WHERE c.threat_id = @threatId
                    ORDER BY c.created_at DESC
                    LIMIT 1";

                using var command = new MySqlConnector.MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@threatId", id);

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return NotFound(new { message = $"No AI rating found for threat {id}" });
                }

                var rating = new
                {
                    threat_id = reader.GetInt32("threat_id"),
                    ai_tier = reader.GetString("ai_tier"),
                    ai_confidence = reader.GetDecimal("ai_confidence"),
                    ai_reasoning = reader.GetString("ai_reasoning"),
                    ai_actions = reader.GetString("ai_actions"),
                    created_at = reader.GetDateTime("created_at")
                };

                return Ok(rating);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving AI rating for threat {id}");
                return StatusCode(500, new { message = "Error retrieving AI rating", error = ex.Message });
            }
        }

        /// <summary>
        /// Manually trigger AI rating for a specific threat
        /// </summary>
        [HttpPost("{id}/rate")]
        public async Task<IActionResult> RateThreat(int id)
        {
            try
            {
                // First, fetch the threat
                using var connection = await _dbService.GetConnectionAsync();
                var query = "SELECT * FROM threats WHERE id = @id";

                using var command = new MySqlConnector.MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                Threat? threat = null;
                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    threat = new Threat
                    {
                        id = reader.GetInt32("id"),
                        title = reader.GetString("title"),
                        description = reader.GetString("description"),
                        category = reader.GetString("category"),
                        source = reader.GetString("source"),
                        date_observed = reader.GetDateTime("date_observed"),
                        impact_level = reader.GetString("impact_level"),
                        external_reference = reader.IsDBNull(reader.GetOrdinal("external_reference"))
                            ? null
                            : reader.GetString("external_reference")
                    };
                }

                if (threat == null)
                {
                    return NotFound(new { message = $"Threat {id} not found" });
                }

                // Classify the threat
                _logger.LogInformation($"Manually rating threat {id}: {threat.title}");
                var classification = await _aiService.ClassifyThreatAsync(threat);

                // Check if classification already exists
                var existingQuery = "SELECT id FROM classifications WHERE threat_id = @threatId";
                using var existingCommand = new MySqlConnector.MySqlCommand(existingQuery, connection);
                existingCommand.Parameters.AddWithValue("@threatId", id);

                var existingId = await existingCommand.ExecuteScalarAsync();

                if (existingId != null)
                {
                    // Update existing classification
                    var updateQuery = @"
                        UPDATE classifications
                        SET ai_tier = @ai_tier,
                            ai_confidence = @ai_confidence,
                            ai_reasoning = @ai_reasoning,
                            ai_actions = @ai_actions,
                            created_at = NOW()
                        WHERE threat_id = @threat_id";

                    using var updateCommand = new MySqlConnector.MySqlCommand(updateQuery, connection);
                    updateCommand.Parameters.AddWithValue("@threat_id", id);
                    updateCommand.Parameters.AddWithValue("@ai_tier", classification.Tier.ToString());
                    updateCommand.Parameters.AddWithValue("@ai_confidence", classification.Confidence);
                    updateCommand.Parameters.AddWithValue("@ai_reasoning", classification.Reasoning);
                    updateCommand.Parameters.AddWithValue("@ai_actions", classification.RecommendedActions);

                    await updateCommand.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Updated AI rating for threat {id}");
                }
                else
                {
                    // Create new classification
                    await _aiService.SaveClassificationAsync(id, classification);
                    _logger.LogInformation($"Created AI rating for threat {id}");
                }

                var result = new
                {
                    threat_id = id,
                    ai_tier = classification.Tier.ToString(),
                    ai_confidence = classification.Confidence,
                    ai_reasoning = classification.Reasoning,
                    ai_actions = classification.RecommendedActions,
                    created_at = DateTime.Now
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rating threat {id}");
                return StatusCode(500, new { message = "Error rating threat", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all threats that don't have AI ratings yet
        /// </summary>
        [HttpGet("unrated")]
        public async Task<IActionResult> GetUnratedThreats(
            [FromQuery] int limit = 50,
            [FromQuery] string? source = null,
            [FromQuery] string? category = null)
        {
            try
            {
                using var connection = await _dbService.GetConnectionAsync();

                var queryBuilder = new System.Text.StringBuilder(@"
                    SELECT t.* FROM threats t
                    LEFT JOIN classifications c ON t.id = c.threat_id
                    WHERE c.id IS NULL");

                if (!string.IsNullOrEmpty(source))
                {
                    queryBuilder.Append(" AND t.source = @source");
                }

                if (!string.IsNullOrEmpty(category))
                {
                    queryBuilder.Append(" AND t.category = @category");
                }

                queryBuilder.Append(" ORDER BY t.date_observed DESC LIMIT @limit");

                using var command = new MySqlConnector.MySqlCommand(queryBuilder.ToString(), connection);
                command.Parameters.AddWithValue("@limit", limit);

                if (!string.IsNullOrEmpty(source))
                {
                    command.Parameters.AddWithValue("@source", source);
                }

                if (!string.IsNullOrEmpty(category))
                {
                    command.Parameters.AddWithValue("@category", category);
                }

                var threats = new List<object>();
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    threats.Add(new
                    {
                        id = reader.GetInt32("id"),
                        title = reader.GetString("title"),
                        description = reader.GetString("description"),
                        category = reader.GetString("category"),
                        source = reader.GetString("source"),
                        date_observed = reader.GetDateTime("date_observed"),
                        impact_level = reader.GetString("impact_level"),
                        external_reference = reader.IsDBNull(reader.GetOrdinal("external_reference"))
                            ? null
                            : reader.GetString("external_reference"),
                        status = reader.GetString("status")
                    });
                }

                return Ok(new
                {
                    count = threats.Count,
                    threats = threats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unrated threats");
                return StatusCode(500, new { message = "Error retrieving unrated threats", error = ex.Message });
            }
        }
    }
}
```

**Step 2: Build to verify no syntax errors**

```bash
dotnet build api/api.csproj
```

Expected: Build succeeds.

**Step 3: Commit**

```bash
git add api/Controllers/ThreatRatingController.cs
git commit -m "feat: add threat rating API endpoints

Added ThreatRatingController with three endpoints:
- GET /api/threats/{id}/ai-rating - View rating for specific threat
- POST /api/threats/{id}/rate - Manually trigger rating
- GET /api/threats/unrated - List unrated threats with filters

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Task 5: Update Configuration

**Files:**
- Modify: `api/appsettings.json`

**Step 1: Add AIRating configuration section**

In `appsettings.json`, add the AIRating section under ThreatIngestion:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "existing connection string..."
  },
  "Gemini": {
    "ApiKey": "your_gemini_api_key_here",
    "Model": "gemini-1.5-pro"
  },
  "ThreatIngestion": {
    "Enabled": true,
    "RunOnStartup": true,
    "AIRating": {
      "Enabled": true,
      "IntervalMinutes": 15,
      "BatchSize": 10,
      "DelayBetweenRequestsSeconds": 4,
      "MaxRetries": 3,
      "RetryDelaySeconds": 30
    },
    "ApiSources": {
      "OTX": {
        "Enabled": true,
        "IntervalMinutes": 60
      },
      "NVD": {
        "Enabled": true,
        "IntervalMinutes": 120
      },
      "CISA": {
        "Enabled": true,
        "IntervalMinutes": 360
      }
    }
  }
}
```

**Step 2: Verify JSON is valid**

```bash
# Use jq or python to validate JSON
python3 -m json.tool api/appsettings.json > /dev/null && echo "Valid JSON" || echo "Invalid JSON"
```

Expected: "Valid JSON"

**Step 3: Commit**

```bash
git add api/appsettings.json
git commit -m "feat: add AI rating configuration

Added AIRating configuration section with:
- Enabled flag for toggle on/off
- IntervalMinutes: 15 (check for unrated threats)
- BatchSize: 10 (threats per batch)
- DelayBetweenRequestsSeconds: 4 (~15 RPM)
- MaxRetries: 3 with exponential backoff
- RetryDelaySeconds: 30 (initial backoff delay)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Task 6: Integration Testing

**Files:**
- None (manual testing)

**Step 1: Start the API**

```bash
dotnet run --project api/api.csproj
```

Expected: API starts without errors, logs show "AI rating scheduled every 15 minutes"

**Step 2: Verify unrated threats endpoint**

```bash
curl http://localhost:5000/api/threats/unrated
```

Expected: JSON response with list of unrated threats (or empty array if all rated)

**Step 3: Manually trigger rating for one threat**

```bash
# Replace {id} with an actual threat ID from your database
curl -X POST http://localhost:5000/api/threats/{id}/rate
```

Expected: JSON response with classification (tier, confidence, reasoning, actions)

**Step 4: Verify rating was saved**

```bash
curl http://localhost:5000/api/threats/{id}/ai-rating
```

Expected: JSON response showing the saved classification

**Step 5: Monitor background service logs**

Watch logs for 15-20 minutes to see automatic rating kick in:

Expected log messages:
- "Starting AI rating batch processing..."
- "Processing X unrated threats"
- "Successfully rated threat {id}"
- "AI Rating: Processed X threats, Y successful, Z failed"

**Step 6: Verify rate limiting works**

If you have many unrated threats, verify logs show delays between requests and exponential backoff on rate limits.

**Step 7: Document any issues found**

If any issues found during testing, create TODO items to fix them.

---

## Task 7: Final Verification and Documentation

**Files:**
- Modify: `docs/plans/2025-12-02-ai-threat-rating-design.md` (add implementation notes)

**Step 1: Run full build**

```bash
dotnet build api/api.csproj
```

Expected: Build succeeds with 0 errors (warnings okay if pre-existing)

**Step 2: Verify all files committed**

```bash
git status
```

Expected: Working directory clean or only untracked files

**Step 3: Create summary commit if needed**

If there are any remaining uncommitted changes:

```bash
git add -A
git commit -m "chore: final cleanup and documentation updates

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

**Step 4: Add implementation notes to design doc**

Add this section to `docs/plans/2025-12-02-ai-threat-rating-design.md`:

```markdown
## Implementation Notes

**Completed:** 2025-12-02

**Changes Made:**
1. Enhanced AIService.cs with Google Search Grounding
2. Added background AI rating timer to ThreatIngestionBackgroundService
3. Implemented ProcessAIRatingsAsync with rate limiting and exponential backoff
4. Created ThreatRatingController with 3 endpoints
5. Added AIRating configuration section to appsettings.json

**Files Modified:**
- api/Services/AIService.cs
- api/Services/ThreatIngestionBackgroundService.cs
- api/appsettings.json

**Files Created:**
- api/Controllers/ThreatRatingController.cs

**Configuration:**
- Default batch size: 10 threats
- Default interval: 15 minutes
- Rate limiting: 4 seconds between requests (~15 RPM)
- Retry strategy: 3 attempts with exponential backoff (30s, 60s, 120s)

**Testing:**
- Manual API endpoint testing completed
- Background service verified running on schedule
- Rate limiting confirmed working
```

**Step 5: Commit documentation update**

```bash
git add docs/plans/2025-12-02-ai-threat-rating-design.md
git commit -m "docs: add implementation notes to design document

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Completion Checklist

- [ ] Google Search Grounding added to AIService
- [ ] Background AI rating timer initialized
- [ ] ProcessAIRatingsAsync method implemented with rate limiting
- [ ] ThreatRatingController created with all endpoints
- [ ] Configuration updated in appsettings.json
- [ ] All changes committed with descriptive messages
- [ ] Integration testing completed
- [ ] Documentation updated

## Next Steps

After implementation:
1. Monitor AI rating logs in production for first 24 hours
2. Adjust batch size and intervals based on threat volume
3. Set Gemini API key via environment variable in production
4. Consider adding metrics/dashboard for rating statistics
5. Review and tune confidence thresholds based on false positive rate

## Notes for Future Engineers

- The `ProcessAIRatingsAsync` method uses exponential backoff specifically for rate limit errors (429 status or "rate limit" in message)
- Default classifications (confidence=0, tier=Medium) are saved when all retries fail to ensure every threat has a classification record
- The Google Search Grounding tool is automatically used by Gemini when included in the tools array - no additional API configuration needed
- Unrated threats are identified by LEFT JOIN on classifications table where classification id is NULL
- The background timer starts with 1-minute delay to allow initial threat ingestion to complete on startup
