# AI Threat Rating System - Design Document

**Date:** 2025-12-02
**Project:** BIO-ISAC Threat Intelligence API
**Feature:** Automatic AI-based threat rating with Google Search Grounding

## Overview

Enhance the existing threat intelligence ingestion system to automatically rate all ingested threats using Google's Gemini AI with Search Grounding for fact-checking. This provides automated, verified threat classification without manual review.

## Architecture

### High-Level Flow

1. **Ingestion Phase** (unchanged): ThreatIngestionBackgroundService syncs threats from OTX, NVD, and CISA. New threats are normalized, deduplicated, and bulk-inserted.

2. **Rating Queue**: Threats without classification records are automatically queued for AI rating.

3. **Background Rating Service**: New timer-based process queries for unrated threats, processes them in configurable batches with rate limiting, and saves results.

4. **Enhanced AI Classification**: Existing AIService upgraded with Google Search Grounding for fact-checking.

5. **API Access**: New endpoints allow viewing ratings, manual triggers, and queue monitoring.

## Components

### 1. Database Schema

**No schema changes required!** Using existing `classifications` table:
- `threat_id` - Foreign key to threats
- `ai_tier` - High/Medium/Low classification
- `ai_confidence` - 0-100 score (enhanced with search grounding)
- `ai_reasoning` - AI explanation with source references
- `ai_actions` - Recommended actions
- `created_at` - Timestamp

**Query for unrated threats:**
```sql
SELECT t.* FROM threats t
LEFT JOIN classifications c ON t.id = c.threat_id
WHERE c.id IS NULL
LIMIT ?;
```

### 2. AIService Enhancements

**Add Google Search Grounding to Gemini API:**
```csharp
var requestBody = new
{
    contents = new[] { /* existing */ },
    generationConfig = new { /* existing */ },
    tools = new[]  // NEW
    {
        new { google_search_retrieval = new { } }
    }
};
```

**Enhanced Prompt Instructions:**
```
Use web search to verify if this threat has been reported by reputable sources.
Adjust your confidence score based on:
- Multiple independent sources confirming: Higher confidence
- No corroborating sources: Lower confidence
- Conflicting information: Note in reasoning
```

**Error Handling:**
Existing try-catch already returns default classification (confidence=50, tier=Medium) on failures.

### 3. Background Processing

**New Timer in ThreatIngestionBackgroundService:**
```csharp
private Timer? _aiRatingTimer;

// Initialize with configurable interval (default: 15 minutes)
_aiRatingTimer = new Timer(
    async _ => await ProcessAIRatingsAsync(),
    null,
    TimeSpan.FromMinutes(1),
    TimeSpan.FromMinutes(aiRatingInterval)
);
```

**ProcessAIRatingsAsync Method:**
- Query unrated threats (configurable batch size, default: 10)
- For each threat:
  - Call `AIService.ClassifyThreatAsync(threat)`
  - Call `AIService.SaveClassificationAsync(threat.id, result)`
  - Delay between requests (configurable, default: 4 seconds = ~15 RPM)
- Implement exponential backoff on 429 rate limit errors:
  - Retry 1: Wait 30 seconds
  - Retry 2: Wait 60 seconds
  - Retry 3: Wait 120 seconds
  - After 3 failures: Save default classification
- Log progress and errors via ILogger

### 4. API Endpoints

**New ThreatRatingController:**

**GET /api/threats/{id}/ai-rating**
- Returns classification for specific threat
- Response: `{ threat_id, ai_tier, ai_confidence, ai_reasoning, ai_actions, created_at }`
- 404 if no rating exists

**POST /api/threats/{id}/rate**
- Manually trigger rating (bypasses queue)
- Updates existing or creates new classification
- Returns classification object

**GET /api/threats/unrated**
- List threats without classifications
- Query params: `?limit=50&source=OTX&category=malware`
- Returns array of threat objects

### 5. Configuration

**appsettings.json additions:**
```json
{
  "Gemini": {
    "ApiKey": "your_api_key_here",
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
      "OTX": { "Enabled": true, "IntervalMinutes": 60 },
      "NVD": { "Enabled": true, "IntervalMinutes": 120 },
      "CISA": { "Enabled": true, "IntervalMinutes": 360 }
    }
  }
}
```

**Configuration Parameters:**
- `AIRating.Enabled`: Toggle auto-rating
- `IntervalMinutes`: Check frequency (default: 15 min)
- `BatchSize`: Threats per batch (default: 10)
- `DelayBetweenRequestsSeconds`: Rate limit control (default: 4s)
- `MaxRetries`: Retry attempts before default save (default: 3)
- `RetryDelaySeconds`: Initial backoff delay, doubles each retry (default: 30s)

**Production Note:** Use environment variables or secrets manager for `Gemini:ApiKey`.

## Implementation Files

### Files to Modify
1. **api/Services/AIService.cs**
   - Add Google Search Grounding to request body
   - Update BuildClassificationPrompt with verification instructions

2. **api/Services/ThreatIngestionBackgroundService.cs**
   - Add _aiRatingTimer field
   - Add ProcessAIRatingsAsync method
   - Add rate limiting and exponential backoff logic

3. **api/appsettings.json**
   - Add Gemini configuration
   - Add AIRating configuration section

### Files to Create
1. **api/Controllers/ThreatRatingController.cs**
   - GET /api/threats/{id}/ai-rating
   - POST /api/threats/{id}/rate
   - GET /api/threats/unrated

## Benefits

1. **Automated Classification**: All threats automatically rated without manual intervention
2. **Fact-Checked Accuracy**: Google Search Grounding verifies threat validity
3. **Rate Limit Safe**: Configurable batching and exponential backoff
4. **Non-Blocking**: Background processing keeps ingestion fast
5. **Flexible**: Full configuration control via appsettings.json
6. **Resilient**: Default classifications on failures ensure all threats are rated

## Success Metrics

- All new threats receive AI classification within 30 minutes of ingestion
- 95%+ successful classification rate (vs default placeholders)
- No ingestion slowdown from AI processing
- No rate limit errors under normal load

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

**Commit History:**
- 245c7fa: feat: add Google Search Grounding to threat classification
- 1a55a16: feat: add AI rating timer infrastructure
- bed353e: feat: implement AI rating with rate limiting and retries
- 5682ed9: feat: add threat rating API endpoints
- 9c00ba5: feat: add AI rating configuration
- 7f1d96f: docs: add comprehensive testing documentation for Task 6
