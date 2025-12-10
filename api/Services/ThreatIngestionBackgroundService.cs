using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Namespace.Services;
using api.Models;
using api.Services;

namespace MyApp.Namespace.Services
{
    public class ThreatIngestionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ThreatIngestionBackgroundService> _logger;

        private Timer? _syncTimer;
        private bool _isRunning = false;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public ThreatIngestionBackgroundService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<ThreatIngestionBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("ThreatIngestion:Enabled", true);
            if (!enabled)
            {
                _logger.LogInformation("Threat ingestion is disabled in configuration");
                return;
            }

            _logger.LogInformation("Threat Ingestion Background Service starting...");
            _logger.LogInformation("Mode: AI-first (threats classified before database storage)");
            _isRunning = true;

            var runOnStartup = _configuration.GetValue<bool>("ThreatIngestion:RunOnStartup", true);
            if (runOnStartup)
            {
                _logger.LogInformation("Running initial sync on startup...");
                await RunInitialSyncAsync();
            }

            // Single timer for all syncs to prevent overlapping and reduce DB load
            var syncInterval = _configuration.GetValue<int>("ThreatIngestion:FetchIntervalMinutes", 60);
            _syncTimer = new Timer(
                async _ => await RunSyncCycleAsync(),
                null,
                TimeSpan.FromMinutes(syncInterval),
                TimeSpan.FromMinutes(syncInterval)
            );
            _logger.LogInformation($"Sync cycle scheduled every {syncInterval} minutes");

            while (!stoppingToken.IsCancellationRequested && _isRunning)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task RunInitialSyncAsync()
        {
            // Run syncs sequentially to avoid overwhelming APIs and DB
            var otxEnabled = _configuration.GetValue<bool>("ThreatIngestion:ApiSources:OTX:Enabled", true);
            var nvdEnabled = _configuration.GetValue<bool>("ThreatIngestion:ApiSources:NVD:Enabled", true);
            var cisaEnabled = _configuration.GetValue<bool>("ThreatIngestion:ApiSources:CISA:Enabled", true);

            if (otxEnabled) await SyncWithAIAsync("OTX");
            if (nvdEnabled) await SyncWithAIAsync("NVD");
            if (cisaEnabled) await SyncWithAIAsync("CISA");
        }

        private async Task RunSyncCycleAsync()
        {
            _logger.LogInformation("Starting scheduled sync cycle...");
            await RunInitialSyncAsync();
            _logger.LogInformation("Sync cycle completed");
        }

        /// <summary>
        /// Fetches threats from API, classifies with AI, then stores only validated threats
        /// </summary>
        private async Task SyncWithAIAsync(string sourceName)
        {
            using var scope = _serviceProvider.CreateScope();
            var normalizationService = scope.ServiceProvider.GetRequiredService<ThreatNormalizationService>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ThreatDeduplicationService>();
            var aiService = scope.ServiceProvider.GetRequiredService<AIService>();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();

            _logger.LogInformation($"Starting AI-first sync for {sourceName}...");

            try
            {
                // Step 1: Fetch from API
                List<Threat> normalizedThreats = await FetchAndNormalizeAsync(sourceName, normalizationService);
                
                if (normalizedThreats.Count == 0)
                {
                    _logger.LogInformation($"{sourceName}: No bio-relevant threats found");
                    return;
                }

                // Step 2: Apply per-source limit
                var maxPerSource = _configuration.GetValue<int>("ThreatIngestion:ApiSources:MaxThreatsPerSource", 25);
                if (normalizedThreats.Count > maxPerSource)
                {
                    _logger.LogInformation($"{sourceName}: Limiting from {normalizedThreats.Count} to {maxPerSource} threats");
                    normalizedThreats = normalizedThreats.Take(maxPerSource).ToList();
                }

                _logger.LogInformation($"{sourceName}: Processing {normalizedThreats.Count} bio-relevant threats, starting AI classification...");

                // Step 3: Check for duplicates (DISABLED - commented out)
                // var existingRefs = await GetExistingExternalReferences(dbService);
                // var newThreats = normalizedThreats
                //     .Where(t => string.IsNullOrEmpty(t.external_reference) || !existingRefs.Contains(t.external_reference))
                //     .ToList();
                // 
                // if (newThreats.Count == 0)
                // {
                //     _logger.LogInformation($"{sourceName}: All {normalizedThreats.Count} threats already exist in database");
                //     return;
                // }

                var newThreats = normalizedThreats; // Process all threats without dedup check
                _logger.LogInformation($"{sourceName}: {newThreats.Count} threats to process");

                // Step 3: Classify each threat with AI before storing
                var delaySeconds = _configuration.GetValue<int>("ThreatIngestion:AIRating:DelayBetweenRequestsSeconds", 10);
                var storedCount = 0;
                var skippedCount = 0;

                foreach (var threat in newThreats)
                {
                    try
                    {
                        _logger.LogInformation($"Classifying: {threat.title}");
                        
                        // Classify with AI
                        var classification = await aiService.ClassifyThreatAsync(threat);
                        
                        // Only store if AI gives reasonable confidence (> 20%) or it's High/Critical tier
                        if (classification.Confidence >= 20 || 
                            classification.Tier == ThreatTier.High)
                        {
                            // Step 4: Store threat AND classification together
                            var threatId = await StoreThreatWithClassificationAsync(
                                dbService, threat, classification);
                            
                            storedCount++;
                            _logger.LogInformation(
                                $"✅ Stored threat {threatId}: {threat.title} " +
                                $"[Tier: {classification.Tier}, Confidence: {classification.Confidence}%]");
                        }
                        else
                        {
                            skippedCount++;
                            _logger.LogInformation(
                                $"⏭️ Skipped low-confidence threat: {threat.title} " +
                                $"[Tier: {classification.Tier}, Confidence: {classification.Confidence}%]");
                        }

                        // Rate limiting between AI calls
                        if (newThreats.IndexOf(threat) < newThreats.Count - 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to classify {threat.title}: {ex.Message}");
                        skippedCount++;
                    }
                }

                _logger.LogInformation(
                    $"✅ {sourceName} complete: {storedCount} threats stored, {skippedCount} skipped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ {sourceName} sync failed: {ex.Message}");
            }
        }

        private async Task<List<Threat>> FetchAndNormalizeAsync(
            string sourceName, 
            ThreatNormalizationService normalizationService)
        {
            using var scope = _serviceProvider.CreateScope();
            
            switch (sourceName)
            {
                case "OTX":
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromSeconds(30);
                        var apiKey = _configuration["OTX:ApiKey"];
                        httpClient.DefaultRequestHeaders.Add("X-OTX-API-KEY", apiKey);
                        var response = await httpClient.GetAsync("https://otx.alienvault.com/api/v1/pulses/subscribed");
                        response.EnsureSuccessStatusCode();
                        var content = await response.Content.ReadAsStringAsync();
                        var data = System.Text.Json.JsonSerializer.Deserialize<object>(content);
                        return normalizationService.NormalizeOTXThreats(data);
                    }

                case "NVD":
                    var nistService = scope.ServiceProvider.GetRequiredService<NISTService>();
                    var nvdData = await nistService.FetchNVDCVEs("");
                    return normalizationService.NormalizeNVDThreats(nvdData);

                case "CISA":
                    var cisaService = scope.ServiceProvider.GetRequiredService<CISAService>();
                    var cisaData = await cisaService.GetKnownExploitedVulnerabilities();
                    return normalizationService.NormalizeCISAThreats(cisaData);

                default:
                    return new List<Threat>();
            }
        }

        private async Task<HashSet<string>> GetExistingExternalReferences(DatabaseService dbService)
        {
            var refs = new HashSet<string>();
            try
            {
                using var connection = await dbService.GetConnectionAsync();
                var query = "SELECT external_reference FROM threats WHERE external_reference IS NOT NULL";
                using var command = new MySqlConnector.MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var refValue = reader.GetString(0);
                    if (!string.IsNullOrEmpty(refValue))
                        refs.Add(refValue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fetch existing references: {ex.Message}");
            }
            return refs;
        }

        private async Task<int> StoreThreatWithClassificationAsync(
            DatabaseService dbService,
            Threat threat,
            ClassificationResult classification)
        {
            using var connection = await dbService.GetConnectionAsync();
            
            // Insert threat
            var threatQuery = @"
                INSERT INTO threats (user_id, title, description, category, source, 
                                    date_observed, impact_level, external_reference, status, created_at) 
                VALUES (@user_id, @title, @description, @category, @source, 
                        @date_observed, @impact_level, @external_reference, 'Pending_Review', NOW())";

            using var threatCmd = new MySqlConnector.MySqlCommand(threatQuery, connection);
            threatCmd.Parameters.AddWithValue("@user_id", threat.user_id ?? 1);
            threatCmd.Parameters.AddWithValue("@title", threat.title);
            threatCmd.Parameters.AddWithValue("@description", threat.description);
            threatCmd.Parameters.AddWithValue("@category", threat.category);
            threatCmd.Parameters.AddWithValue("@source", threat.source);
            threatCmd.Parameters.AddWithValue("@date_observed", threat.date_observed);
            threatCmd.Parameters.AddWithValue("@impact_level", threat.impact_level);
            threatCmd.Parameters.AddWithValue("@external_reference", 
                string.IsNullOrEmpty(threat.external_reference) ? DBNull.Value : threat.external_reference);

            await threatCmd.ExecuteNonQueryAsync();
            var threatId = (int)threatCmd.LastInsertedId;

            // Insert classification
            var classQuery = @"
                INSERT INTO classifications (threat_id, ai_tier, ai_confidence, ai_reasoning, ai_actions, ai_next_steps, ai_recommended_industry) 
                VALUES (@threat_id, @ai_tier, @ai_confidence, @ai_reasoning, @ai_actions, @ai_next_steps, @ai_recommended_industry)";

            using var classCmd = new MySqlConnector.MySqlCommand(classQuery, connection);
            classCmd.Parameters.AddWithValue("@threat_id", threatId);
            classCmd.Parameters.AddWithValue("@ai_tier", classification.Tier.ToString());
            classCmd.Parameters.AddWithValue("@ai_confidence", classification.Confidence);
            classCmd.Parameters.AddWithValue("@ai_reasoning", classification.Reasoning);
            classCmd.Parameters.AddWithValue("@ai_actions", classification.RecommendedActions ?? (object)DBNull.Value);
            
            // Save NextSteps as JSON array to ai_next_steps
            var nextStepsJson = classification.NextSteps != null && classification.NextSteps.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(classification.NextSteps, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                })
                : null;
            classCmd.Parameters.AddWithValue("@ai_next_steps", (object?)nextStepsJson ?? DBNull.Value);
            // Build industry string: if Other, use "Other: {specificIndustry}", otherwise use recommendedIndustry
            var industryToStore = classification.RecommendedIndustry;
            if (industryToStore != null && industryToStore.Equals("Other", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(classification.SpecificIndustry))
            {
                industryToStore = $"Other: {classification.SpecificIndustry}";
            }
            classCmd.Parameters.AddWithValue("@ai_recommended_industry", string.IsNullOrEmpty(industryToStore) ? (object)DBNull.Value : industryToStore);

            await classCmd.ExecuteNonQueryAsync();

            return threatId;
        }

        public async Task SyncAllAsync()
        {
            _logger.LogInformation("Manual sync triggered...");
            await RunInitialSyncAsync();
            _logger.LogInformation("Manual sync completed");
        }

        public void Stop()
        {
            _isRunning = false;
            _syncTimer?.Dispose();
            _cancellationTokenSource.Cancel();
            _logger.LogInformation("Threat Ingestion Background Service stopped");
        }

        public override void Dispose()
        {
            _syncTimer?.Dispose();
            _cancellationTokenSource.Dispose();
            base.Dispose();
        }
    }
}
