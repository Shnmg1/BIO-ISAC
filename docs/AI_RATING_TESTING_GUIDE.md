# AI Threat Rating - Integration Testing Guide

**Date:** 2025-12-02
**Status:** Testing Documentation for Deployment
**Related Plan:** docs/plans/2025-12-02-ai-threat-rating-implementation.md (Task 6)

## Build Verification Results

### Build Status: SUCCESSFUL
- **Command:** `dotnet build api/api.csproj`
- **Errors:** 0
- **Warnings:** 7 (pre-existing, unrelated to AI rating implementation)
- **Build Time:** ~1.18 seconds
- **Output:** api.dll successfully generated

### Code Implementation Verification

All required components for AI threat rating have been implemented and verified:

#### 1. Google Search Grounding (Task 1)
- **File:** api/Services/AIService.cs
- **Status:** Implemented
- **Feature:** `google_search_retrieval` tool added to Gemini API request
- **Verification:** Pattern match confirmed on line 73

#### 2. Background AI Rating Timer (Task 2)
- **File:** api/Services/ThreatIngestionBackgroundService.cs
- **Status:** Implemented
- **Features:**
  - Timer field `_aiRatingTimer` added (line 18)
  - Timer initialization in ExecuteAsync method (line 83)
  - Proper disposal in Stop() and Dispose() methods (lines 439, 449)
- **Verification:** All timer references confirmed

#### 3. ProcessAIRatingsAsync Method (Task 3)
- **File:** api/Services/ThreatIngestionBackgroundService.cs
- **Status:** Implemented
- **Location:** Line 254
- **Features:**
  - Batch processing of unrated threats
  - Rate limiting with configurable delays
  - Exponential backoff retry logic
  - Default classification on failure

#### 4. ThreatRatingController (Task 4)
- **File:** api/Controllers/ThreatRatingController.cs
- **Status:** Created
- **Size:** 9,985 bytes
- **Endpoints:** 3 endpoints implemented
- **Last Modified:** Dec 2, 2025 09:25

#### 5. Configuration (Task 5)
- **File:** api/appsettings.json
- **Status:** Updated
- **AIRating Section Verified:**
  - Enabled: true
  - IntervalMinutes: 15
  - BatchSize: 10
  - DelayBetweenRequestsSeconds: 4
  - MaxRetries: 3
  - RetryDelaySeconds: 30

## Deployment Testing Checklist

### Pre-Deployment Verification

- [x] **Build Compilation:** API builds successfully with no errors
- [x] **All Implementation Files Present:**
  - [x] AIService.cs modified with Search Grounding
  - [x] ThreatIngestionBackgroundService.cs modified with timer
  - [x] ThreatRatingController.cs created
  - [x] appsettings.json updated with AIRating config
- [x] **Configuration Validation:** AIRating section properly formatted

### Environment Setup Checklist

Before running tests in deployed environment:

- [ ] **Database Connection:** Verify MySQL connection string in appsettings.json
- [ ] **Gemini API Key:** Set environment variable or update appsettings.json
- [ ] **Database Schema:** Ensure `threats` and `classifications` tables exist
- [ ] **Sample Data:** Load test threats into database (some rated, some unrated)

### Step 1: API Startup Testing

**Command:**
```bash
cd api
dotnet run --project api.csproj
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[0]
      Now listening on: http://localhost:5000
info: api.Services.ThreatIngestionBackgroundService[0]
      AI rating scheduled every 15 minutes
```

**Verification Checklist:**
- [ ] API starts without exceptions
- [ ] Log message "AI rating scheduled every 15 minutes" appears
- [ ] Background service initializes successfully
- [ ] No database connection errors on startup
- [ ] Swagger UI accessible at http://localhost:5000/swagger (if enabled)

**Potential Issues:**
- **Missing Gemini API Key:** Will cause classification failures (not startup failures)
- **Database Connection:** Will prevent API from starting
- **Port Conflict:** Change port in launchSettings.json if 5000 is in use

### Step 2: Unrated Threats Endpoint Testing

**Endpoint:** `GET /api/threats/unrated`

**Test Cases:**

#### Test 2.1: Basic Unrated Threats Query
```bash
curl http://localhost:5000/api/threats/unrated
```

**Expected Response:**
```json
{
  "count": <number>,
  "threats": [
    {
      "id": 123,
      "title": "Sample Threat",
      "description": "...",
      "category": "malware",
      "source": "OTX",
      "date_observed": "2025-12-01T00:00:00",
      "impact_level": "High",
      "external_reference": "...",
      "status": "new"
    }
  ]
}
```

**Verification:**
- [ ] Status code: 200 OK
- [ ] Response contains count field
- [ ] Response contains threats array
- [ ] All threats in response have no classification in database
- [ ] Threats ordered by date_observed DESC

#### Test 2.2: Unrated Threats with Limit
```bash
curl "http://localhost:5000/api/threats/unrated?limit=5"
```

**Expected:**
- [ ] Returns maximum 5 threats
- [ ] Status code: 200 OK

#### Test 2.3: Unrated Threats with Source Filter
```bash
curl "http://localhost:5000/api/threats/unrated?source=OTX"
curl "http://localhost:5000/api/threats/unrated?source=NVD"
curl "http://localhost:5000/api/threats/unrated?source=CISA"
```

**Expected:**
- [ ] Returns only threats from specified source
- [ ] Status code: 200 OK

#### Test 2.4: Unrated Threats with Category Filter
```bash
curl "http://localhost:5000/api/threats/unrated?category=malware"
```

**Expected:**
- [ ] Returns only threats with specified category
- [ ] Status code: 200 OK

#### Test 2.5: Combined Filters
```bash
curl "http://localhost:5000/api/threats/unrated?source=OTX&category=malware&limit=10"
```

**Expected:**
- [ ] Returns threats matching all filters
- [ ] Status code: 200 OK

#### Test 2.6: No Unrated Threats
```bash
# Run after all threats are rated
curl http://localhost:5000/api/threats/unrated
```

**Expected Response:**
```json
{
  "count": 0,
  "threats": []
}
```

**Verification:**
- [ ] Status code: 200 OK
- [ ] Empty array returned, not error

### Step 3: Manual Threat Rating Testing

**Endpoint:** `POST /api/threats/{id}/rate`

**Prerequisites:**
- Get a valid unrated threat ID from Step 2

#### Test 3.1: Rate Unrated Threat
```bash
# Replace {id} with actual threat ID (e.g., 123)
curl -X POST http://localhost:5000/api/threats/{id}/rate
```

**Expected Response:**
```json
{
  "threat_id": 123,
  "ai_tier": "High" | "Medium" | "Low",
  "ai_confidence": 85.5,
  "ai_reasoning": "Detailed explanation with source references...",
  "ai_actions": "Recommended actions...",
  "created_at": "2025-12-02T10:30:00"
}
```

**Verification:**
- [ ] Status code: 200 OK
- [ ] Response contains all required fields
- [ ] ai_tier is one of: High, Medium, Low
- [ ] ai_confidence is between 0-100
- [ ] ai_reasoning contains fact-checking references (Google Search Grounding)
- [ ] created_at is current timestamp
- [ ] Classification saved in database (verify in classifications table)

**Expected Logs:**
```
info: api.Controllers.ThreatRatingController[0]
      Manually rating threat 123: Sample Threat Title
info: api.Controllers.ThreatRatingController[0]
      Created AI rating for threat 123
```

#### Test 3.2: Re-rate Already Rated Threat
```bash
# Same threat ID as Test 3.1
curl -X POST http://localhost:5000/api/threats/{id}/rate
```

**Expected:**
- [ ] Status code: 200 OK
- [ ] New classification overwrites old one
- [ ] created_at timestamp updated

**Expected Logs:**
```
info: api.Controllers.ThreatRatingController[0]
      Updated AI rating for threat 123
```

#### Test 3.3: Rate Non-existent Threat
```bash
curl -X POST http://localhost:5000/api/threats/999999/rate
```

**Expected Response:**
```json
{
  "message": "Threat 999999 not found"
}
```

**Verification:**
- [ ] Status code: 404 Not Found

#### Test 3.4: Rate with Invalid Gemini API Key
**Setup:** Temporarily set invalid API key in configuration

**Expected:**
- [ ] Status code: 500 Internal Server Error
- [ ] Error message indicates API key issue
- [ ] Error logged to console

### Step 4: Get AI Rating Endpoint Testing

**Endpoint:** `GET /api/threats/{id}/ai-rating`

#### Test 4.1: Get Existing Rating
```bash
# Use threat ID that was rated in Step 3
curl http://localhost:5000/api/threats/{id}/ai-rating
```

**Expected Response:**
```json
{
  "threat_id": 123,
  "ai_tier": "High",
  "ai_confidence": 85.5,
  "ai_reasoning": "...",
  "ai_actions": "...",
  "created_at": "2025-12-02T10:30:00"
}
```

**Verification:**
- [ ] Status code: 200 OK
- [ ] Returns most recent classification
- [ ] Data matches what was saved in Step 3

#### Test 4.2: Get Rating for Unrated Threat
```bash
# Use threat ID that has never been rated
curl http://localhost:5000/api/threats/{unrated_id}/ai-rating
```

**Expected Response:**
```json
{
  "message": "No AI rating found for threat {unrated_id}"
}
```

**Verification:**
- [ ] Status code: 404 Not Found

#### Test 4.3: Get Rating for Non-existent Threat
```bash
curl http://localhost:5000/api/threats/999999/ai-rating
```

**Expected:**
- [ ] Status code: 404 Not Found

### Step 5: Background Service Monitoring

**Duration:** Monitor for at least 15-20 minutes

**What to Watch:**

#### At T+1 minute (Initial Run)
```
info: api.Services.ThreatIngestionBackgroundService[0]
      Starting AI rating batch processing...
info: api.Services.ThreatIngestionBackgroundService[0]
      Processing X unrated threats
info: api.Services.ThreatIngestionBackgroundService[0]
      Rating threat 123: Sample Threat Title
info: api.Services.ThreatIngestionBackgroundService[0]
      Successfully rated threat 123
info: api.Services.ThreatIngestionBackgroundService[0]
      AI Rating: Processed X threats, Y successful, Z failed (saved defaults)
```

**Verification:**
- [ ] First run occurs 1 minute after startup
- [ ] Batch processing logs appear
- [ ] Number of threats processed matches BatchSize config (max 10)
- [ ] Success/failure counts logged
- [ ] Audit log entry created

#### At T+16 minutes (Second Run)
```
info: api.Services.ThreatIngestionBackgroundService[0]
      Starting AI rating batch processing...
```

**Verification:**
- [ ] Timer runs every 15 minutes as configured
- [ ] Each run processes up to 10 threats
- [ ] Timer continues running indefinitely

#### If No Unrated Threats
```
info: api.Services.ThreatIngestionBackgroundService[0]
      Starting AI rating batch processing...
info: api.Services.ThreatIngestionBackgroundService[0]
      No unrated threats found
```

**Verification:**
- [ ] Gracefully handles empty queue
- [ ] No errors thrown

### Step 6: Rate Limiting Verification

**Prerequisites:** Have more than 10 unrated threats in database

**Setup:**
1. Temporarily modify config to increase batch size:
```json
"BatchSize": 20,
"DelayBetweenRequestsSeconds": 2
```
2. Restart API

**What to Watch:**

```
info: api.Services.ThreatIngestionBackgroundService[0]
      Rating threat 1: ...
[2 second delay]
info: api.Services.ThreatIngestionBackgroundService[0]
      Rating threat 2: ...
[2 second delay]
info: api.Services.ThreatIngestionBackgroundService[0]
      Rating threat 3: ...
```

**Verification:**
- [ ] Delay occurs between each request (not before first or after last)
- [ ] Delay duration matches DelayBetweenRequestsSeconds config
- [ ] Approximately 15 requests per minute (with 4-second delay)

### Step 7: Retry Logic and Error Handling

#### Test 7.1: Rate Limit Error Handling
**Setup:** Temporarily set DelayBetweenRequestsSeconds to 0 to trigger rate limits faster

**Expected Logs:**
```
warn: api.Services.ThreatIngestionBackgroundService[0]
      Rate limit hit for threat 123, retry 1/3 after 30s: ...
[30 second delay]
warn: api.Services.ThreatIngestionBackgroundService[0]
      Rate limit hit for threat 123, retry 2/3 after 60s: ...
[60 second delay]
warn: api.Services.ThreatIngestionBackgroundService[0]
      Rate limit hit for threat 123, retry 3/3 after 120s: ...
```

**Verification:**
- [ ] Exponential backoff occurs (30s, 60s, 120s)
- [ ] Retries up to 3 times
- [ ] After 3 failures, saves default classification

#### Test 7.2: Default Classification on Failure
**Expected:** After max retries, default classification saved:
- Tier: Medium
- Confidence: 0
- Reasoning: Contains error message and "Manual review required"

**Database Verification:**
```sql
SELECT * FROM classifications WHERE confidence = 0 AND ai_reasoning LIKE '%Manual review required%';
```

**Verification:**
- [ ] Default classification exists in database
- [ ] Threat is no longer returned by /api/threats/unrated
- [ ] Audit log shows failure recorded

### Step 8: Database Verification

**Queries to Run:**

#### Count Unrated Threats
```sql
SELECT COUNT(*) FROM threats t
LEFT JOIN classifications c ON t.id = c.threat_id
WHERE c.id IS NULL;
```

**Expected:** Count decreases over time as background service processes threats

#### View Recent Classifications
```sql
SELECT * FROM classifications
ORDER BY created_at DESC
LIMIT 10;
```

**Verification:**
- [ ] New classifications appear every 15 minutes
- [ ] All required fields populated
- [ ] created_at timestamps are recent

#### Check for Failed Classifications
```sql
SELECT * FROM classifications
WHERE confidence = 0
AND ai_reasoning LIKE '%failed%';
```

**Verification:**
- [ ] Failed threats have default classifications
- [ ] Error messages captured in ai_reasoning

### Step 9: Google Search Grounding Verification

**Manual Review:** Check classification reasoning for search references

**Example Expected Reasoning:**
```
"This threat has been confirmed by multiple security vendors including
[Vendor Name] and [CERT Team]. The vulnerability affects biological
research systems as reported in [Advisory Name]. Confidence is high
due to corroboration from independent sources..."
```

**Verification:**
- [ ] Reasoning references external sources
- [ ] Mentions specific vendors, advisories, or organizations
- [ ] Confidence score correlates with number of sources found
- [ ] High confidence (80-100%): Multiple sources mentioned
- [ ] Medium confidence (50-79%): Single source or some verification
- [ ] Low confidence (0-49%): No sources or conflicting info

### Step 10: Performance Monitoring

**Metrics to Track:**

#### API Response Times
```bash
# Measure response time for each endpoint
time curl http://localhost:5000/api/threats/unrated
time curl http://localhost:5000/api/threats/{id}/ai-rating
time curl -X POST http://localhost:5000/api/threats/{id}/rate
```

**Expected:**
- [ ] GET /unrated: < 500ms (depends on database size)
- [ ] GET /{id}/ai-rating: < 200ms
- [ ] POST /{id}/rate: 2-5 seconds (depends on Gemini API)

#### Background Processing Time
Monitor logs for batch processing duration:
- [ ] Batch of 10 threats: ~40-50 seconds (with 4s delay between requests)
- [ ] Single classification: 2-5 seconds average

#### Resource Usage
```bash
# Monitor during background processing
top -pid $(pgrep -f "dotnet.*api.dll")
```

**Expected:**
- [ ] CPU usage spikes during batch processing (normal)
- [ ] Memory stable (no leaks over multiple cycles)
- [ ] No connection pool exhaustion

## Testing Results Template

Copy and fill out after running tests:

```
## Test Execution Results
**Date:** [DATE]
**Tester:** [NAME]
**Environment:** [Development/Staging/Production]
**Database:** MySQL [VERSION]

### Build Verification
- [x] API builds successfully: YES/NO
- [x] Errors: [COUNT]
- [x] Warnings: [COUNT]

### Endpoint Testing
- [ ] GET /api/threats/unrated: PASS/FAIL
  - Notes: [details]
- [ ] POST /api/threats/{id}/rate: PASS/FAIL
  - Notes: [details]
- [ ] GET /api/threats/{id}/ai-rating: PASS/FAIL
  - Notes: [details]

### Background Service
- [ ] Timer starts correctly: YES/NO
- [ ] Log message appears: YES/NO
- [ ] First run at T+1 min: YES/NO
- [ ] Subsequent runs at 15-min intervals: YES/NO
- [ ] Batch processing successful: YES/NO
  - Processed: [X] threats
  - Success: [Y]
  - Failed: [Z]

### Rate Limiting
- [ ] Delays between requests observed: YES/NO
- [ ] Delay duration correct: YES/NO
- [ ] Exponential backoff on errors: YES/NO

### Google Search Grounding
- [ ] Reasoning includes source references: YES/NO
- [ ] Confidence correlates with sources: YES/NO

### Issues Found
1. [Description]
2. [Description]

### Recommendations
1. [Recommendation]
2. [Recommendation]
```

## Known Issues and Limitations

### Current Build Warnings
The following warnings exist in the codebase (pre-existing, not related to AI rating):
- CS7022: Entry point warning in AIConfidenceController.cs
- CS8618: Non-nullable property warnings (3 instances)
- CS8604: Null reference argument warning
- CS8603: Null reference return warnings (2 instances)

**Impact:** None on AI rating functionality
**Action:** Address in separate code quality improvement task

### Dependencies
- **Gemini API Key Required:** Classification will fail without valid key
- **Database Must Be Running:** API won't start without MySQL connection
- **Internet Connection Required:** Google Search Grounding needs network access

### Rate Limits
- **Gemini API:** 15 RPM (handled by 4-second delay)
- **Google Search Grounding:** Included in Gemini quota
- **Adjust if needed:** Modify DelayBetweenRequestsSeconds in config

## Troubleshooting Guide

### Issue: API Doesn't Start
**Symptoms:** Exception on startup
**Possible Causes:**
- Database connection string incorrect
- MySQL server not running
- Port 5000 already in use

**Solutions:**
1. Check connection string in appsettings.json
2. Verify MySQL is running: `systemctl status mysql`
3. Change port in launchSettings.json

### Issue: No Log Message "AI rating scheduled every 15 minutes"
**Possible Causes:**
- AIRating.Enabled = false in config
- Exception during timer initialization

**Solutions:**
1. Check appsettings.json: Ensure "Enabled": true
2. Check logs for exceptions
3. Verify configuration section exists

### Issue: Background Service Not Running
**Symptoms:** No batch processing logs after 1 minute
**Possible Causes:**
- Timer initialization failed
- Background service not registered in DI

**Solutions:**
1. Check for exceptions in logs
2. Verify ThreatIngestionBackgroundService is registered in Program.cs
3. Check timer disposal isn't being called prematurely

### Issue: Classification Fails with 401/403
**Symptoms:** Error response from Gemini API
**Cause:** Invalid or missing API key

**Solutions:**
1. Set environment variable: `export GEMINI_API_KEY=your_key_here`
2. Update appsettings.json with valid key
3. Verify key has required permissions

### Issue: All Classifications Have Confidence 0
**Symptoms:** Default classifications being saved for all threats
**Possible Causes:**
- Gemini API failures
- Network issues
- Rate limit exceeded

**Solutions:**
1. Check error logs for specific API errors
2. Verify internet connectivity
3. Increase DelayBetweenRequestsSeconds
4. Reduce BatchSize

### Issue: Search Grounding Not Working
**Symptoms:** No source references in reasoning
**Possible Causes:**
- Tool not configured correctly in request
- Gemini model doesn't support Search Grounding
- Network/firewall blocking Google Search

**Solutions:**
1. Verify `google_search_retrieval` in AIService.cs requestBody
2. Check Gemini model is gemini-1.5-pro (other models may not support it)
3. Check network access to Google services

## Post-Deployment Monitoring

### First 24 Hours
- [ ] Monitor error rate in logs
- [ ] Track number of threats classified
- [ ] Verify no memory leaks
- [ ] Check audit log entries
- [ ] Review classification quality (spot check)

### First Week
- [ ] Analyze confidence score distribution
- [ ] Review false positive rate
- [ ] Adjust batch size if needed
- [ ] Adjust interval if needed
- [ ] Review and tune tier thresholds

### Ongoing
- [ ] Weekly review of failed classifications
- [ ] Monthly review of classification accuracy
- [ ] Quarterly review of configuration settings
- [ ] Monitor Gemini API usage and costs

## Success Criteria

Deployment is considered successful if:
- [x] API builds and starts without errors
- [ ] All three endpoints return correct responses
- [ ] Background service processes threats every 15 minutes
- [ ] Rate limiting prevents API quota exhaustion
- [ ] Default classifications saved on failures
- [ ] Google Search Grounding provides source references
- [ ] No critical errors in logs after 24 hours

## Next Steps After Testing

1. **If Tests Pass:**
   - Deploy to staging environment
   - Run same test suite in staging
   - Monitor for 24 hours
   - Deploy to production with monitoring

2. **If Tests Fail:**
   - Document all failures in GitHub Issues
   - Fix critical issues first
   - Re-run failed tests
   - Repeat until all tests pass

3. **Production Deployment:**
   - Set Gemini API key via environment variable (not config file)
   - Configure monitoring/alerting for background service
   - Set up dashboard for classification metrics
   - Enable audit log review workflow

## Appendix: Configuration Reference

### Default Configuration Values
```json
{
  "ThreatIngestion": {
    "AIRating": {
      "Enabled": true,
      "IntervalMinutes": 15,
      "BatchSize": 10,
      "DelayBetweenRequestsSeconds": 4,
      "MaxRetries": 3,
      "RetryDelaySeconds": 30
    }
  }
}
```

### Recommended Production Settings
```json
{
  "ThreatIngestion": {
    "AIRating": {
      "Enabled": true,
      "IntervalMinutes": 15,        // Adjust based on threat volume
      "BatchSize": 10,               // Don't increase beyond 15 (rate limit)
      "DelayBetweenRequestsSeconds": 4,  // Maintains ~15 RPM
      "MaxRetries": 3,
      "RetryDelaySeconds": 30
    }
  }
}
```

### High Volume Settings (>1000 threats/day)
```json
{
  "ThreatIngestion": {
    "AIRating": {
      "Enabled": true,
      "IntervalMinutes": 10,        // More frequent runs
      "BatchSize": 15,              // Maximum safe batch size
      "DelayBetweenRequestsSeconds": 4,
      "MaxRetries": 3,
      "RetryDelaySeconds": 30
    }
  }
}
```

### Low Volume Settings (<100 threats/day)
```json
{
  "ThreatIngestion": {
    "AIRating": {
      "Enabled": true,
      "IntervalMinutes": 30,        // Less frequent runs
      "BatchSize": 5,
      "DelayBetweenRequestsSeconds": 2,
      "MaxRetries": 3,
      "RetryDelaySeconds": 30
    }
  }
}
```

## Contact and Support

**Implementation:** Claude Code (AI Assistant)
**Documentation Date:** 2025-12-02
**Related Documents:**
- docs/plans/2025-12-02-ai-threat-rating-implementation.md
- docs/plans/2025-12-02-ai-threat-rating-design.md
