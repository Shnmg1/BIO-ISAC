using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Namespace.Services;

namespace MyApp.Namespace.Services
{
    public class ThreatIngestionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ThreatIngestionBackgroundService> _logger;

        private Timer? _otxTimer;
        private Timer? _nvdTimer;
        private Timer? _cisaTimer;
        private Timer? _aiRatingTimer;
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
            _isRunning = true;

            var runOnStartup = _configuration.GetValue<bool>("ThreatIngestion:RunOnStartup", true);
            if (runOnStartup)
            {
                _logger.LogInformation("Running initial sync on startup...");
                await RunInitialSyncAsync();
            }

            var otxInterval = _configuration.GetValue<int>("ThreatIngestion:ApiSources:OTX:IntervalMinutes", 60);
            var nvdInterval = _configuration.GetValue<int>("ThreatIngestion:ApiSources:NVD:IntervalMinutes", 120);
            var cisaInterval = _configuration.GetValue<int>("ThreatIngestion:ApiSources:CISA:IntervalMinutes", 360);

            var otxEnabled = _configuration.GetValue<bool>("ThreatIngestion:ApiSources:OTX:Enabled", true);
            var nvdEnabled = _configuration.GetValue<bool>("ThreatIngestion:ApiSources:NVD:Enabled", true);
            var cisaEnabled = _configuration.GetValue<bool>("ThreatIngestion:ApiSources:CISA:Enabled", true);

            if (otxEnabled)
            {
                _otxTimer = new Timer(async _ => await SyncOTXAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(otxInterval));
                _logger.LogInformation($"OTX sync scheduled every {otxInterval} minutes");
            }

            if (nvdEnabled)
            {
                _nvdTimer = new Timer(async _ => await SyncNVDAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(nvdInterval));
                _logger.LogInformation($"NVD sync scheduled every {nvdInterval} minutes");
            }

            if (cisaEnabled)
            {
                _cisaTimer = new Timer(async _ => await SyncCISAAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(cisaInterval));
                _logger.LogInformation($"CISA sync scheduled every {cisaInterval} minutes");
            }

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

            while (!stoppingToken.IsCancellationRequested && _isRunning)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task RunInitialSyncAsync()
        {
            var tasks = new List<Task>();

            if (_configuration.GetValue<bool>("ThreatIngestion:ApiSources:OTX:Enabled", true))
            {
                tasks.Add(SyncOTXAsync());
            }

            if (_configuration.GetValue<bool>("ThreatIngestion:ApiSources:NVD:Enabled", true))
            {
                tasks.Add(SyncNVDAsync());
            }

            if (_configuration.GetValue<bool>("ThreatIngestion:ApiSources:CISA:Enabled", true))
            {
                tasks.Add(SyncCISAAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task SyncOTXAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var auditLogService = scope.ServiceProvider.GetRequiredService<AuditLogService>();
            var normalizationService = scope.ServiceProvider.GetRequiredService<ThreatNormalizationService>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ThreatDeduplicationService>();
            var apiSourceService = scope.ServiceProvider.GetRequiredService<ApiSourceService>();

            var sourceName = "OTX";
            _logger.LogInformation($"Starting threat ingestion sync for {sourceName}...");

            try
            {
                await auditLogService.LogAsync("api_sync_started", $"Starting {sourceName} sync");

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                var apiKey = _configuration["OTX:ApiKey"] ?? "4454bd50246c8265987afd9f6c73cf99000e18e73c7abfe1f6a4364d815b2201";
                httpClient.DefaultRequestHeaders.Add("X-OTX-API-KEY", apiKey);

                var response = await httpClient.GetAsync("https://otx.alienvault.com/api/v1/pulses/subscribed");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var otxData = System.Text.Json.JsonSerializer.Deserialize<object>(content);

                var normalizedThreats = normalizationService.NormalizeOTXThreats(otxData);
                var dedupResult = await deduplicationService.ProcessThreatsAsync(normalizedThreats);

                var newCount = await deduplicationService.BulkInsertThreatsAsync(dedupResult.NewThreats);
                var updatedCount = await deduplicationService.BulkUpdateThreatsAsync(dedupResult.UpdatedThreats);

                var totalFetched = normalizedThreats.Count;
                var message = $"{sourceName}: Fetched {totalFetched} threats, {newCount} new, {updatedCount} updated, {dedupResult.SkippedThreats.Count} duplicates";

                await apiSourceService.UpdateSyncStatusAsync(sourceName, true, null, newCount);
                await auditLogService.LogAsync("api_sync_completed", message);

                _logger.LogInformation($"✅ {message}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"{sourceName} sync failed: {ex.Message}";
                _logger.LogError(ex, $"❌ {errorMsg}");

                await apiSourceService.UpdateSyncStatusAsync(sourceName, false, errorMsg, 0);
                await auditLogService.LogAsync("api_sync_failed", errorMsg);
            }
        }

        public async Task SyncNVDAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var nistService = scope.ServiceProvider.GetRequiredService<NISTService>();
            var auditLogService = scope.ServiceProvider.GetRequiredService<AuditLogService>();
            var normalizationService = scope.ServiceProvider.GetRequiredService<ThreatNormalizationService>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ThreatDeduplicationService>();
            var apiSourceService = scope.ServiceProvider.GetRequiredService<ApiSourceService>();

            var sourceName = "NVD";
            _logger.LogInformation($"Starting threat ingestion sync for {sourceName}...");

            try
            {
                await auditLogService.LogAsync("api_sync_started", $"Starting {sourceName} sync");

                var nvdData = await nistService.FetchNVDCVEs("");

                var normalizedThreats = normalizationService.NormalizeNVDThreats(nvdData);
                var dedupResult = await deduplicationService.ProcessThreatsAsync(normalizedThreats);

                var newCount = await deduplicationService.BulkInsertThreatsAsync(dedupResult.NewThreats);
                var updatedCount = await deduplicationService.BulkUpdateThreatsAsync(dedupResult.UpdatedThreats);

                var totalFetched = normalizedThreats.Count;
                var message = $"{sourceName}: Fetched {totalFetched} threats, {newCount} new, {updatedCount} updated, {dedupResult.SkippedThreats.Count} duplicates";

                await apiSourceService.UpdateSyncStatusAsync(sourceName, true, null, newCount);
                await auditLogService.LogAsync("api_sync_completed", message);

                _logger.LogInformation($"✅ {message}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"{sourceName} sync failed: {ex.Message}";
                _logger.LogError(ex, $"❌ {errorMsg}");

                await apiSourceService.UpdateSyncStatusAsync(sourceName, false, errorMsg, 0);
                await auditLogService.LogAsync("api_sync_failed", errorMsg);
            }
        }

        public async Task SyncCISAAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var cisaService = scope.ServiceProvider.GetRequiredService<CISAService>();
            var auditLogService = scope.ServiceProvider.GetRequiredService<AuditLogService>();
            var normalizationService = scope.ServiceProvider.GetRequiredService<ThreatNormalizationService>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ThreatDeduplicationService>();
            var apiSourceService = scope.ServiceProvider.GetRequiredService<ApiSourceService>();

            var sourceName = "CISA";
            _logger.LogInformation($"Starting threat ingestion sync for {sourceName}...");

            try
            {
                await auditLogService.LogAsync("api_sync_started", $"Starting {sourceName} sync");

                var cisaData = await cisaService.GetKnownExploitedVulnerabilities();

                var normalizedThreats = normalizationService.NormalizeCISAThreats(cisaData);
                var dedupResult = await deduplicationService.ProcessThreatsAsync(normalizedThreats);

                var newCount = await deduplicationService.BulkInsertThreatsAsync(dedupResult.NewThreats);
                var updatedCount = await deduplicationService.BulkUpdateThreatsAsync(dedupResult.UpdatedThreats);

                var totalFetched = normalizedThreats.Count;
                var message = $"{sourceName}: Fetched {totalFetched} threats, {newCount} new, {updatedCount} updated, {dedupResult.SkippedThreats.Count} duplicates";

                await apiSourceService.UpdateSyncStatusAsync(sourceName, true, null, newCount);
                await auditLogService.LogAsync("api_sync_completed", message);

                _logger.LogInformation($"✅ {message}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"{sourceName} sync failed: {ex.Message}";
                _logger.LogError(ex, $"❌ {errorMsg}");

                await apiSourceService.UpdateSyncStatusAsync(sourceName, false, errorMsg, 0);
                await auditLogService.LogAsync("api_sync_failed", errorMsg);
            }
        }

        public async Task ProcessAIRatingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var aiService = scope.ServiceProvider.GetRequiredService<api.Services.AIService>();
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
                                var defaultClassification = new api.Services.ClassificationResult
                                {
                                    Tier = api.Models.ThreatTier.Medium,
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

                                var defaultClassification = new api.Services.ClassificationResult
                                {
                                    Tier = api.Models.ThreatTier.Medium,
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

        public async Task SyncAllAsync()
        {
            var tasks = new List<Task>
            {
                SyncOTXAsync(),
                SyncNVDAsync(),
                SyncCISAAsync()
            };

            await Task.WhenAll(tasks);
        }

        public void Stop()
        {
            _isRunning = false;
            _otxTimer?.Dispose();
            _nvdTimer?.Dispose();
            _cisaTimer?.Dispose();
            _aiRatingTimer?.Dispose();
            _cancellationTokenSource.Cancel();
            _logger.LogInformation("Threat Ingestion Background Service stopped");
        }

        public override void Dispose()
        {
            _otxTimer?.Dispose();
            _nvdTimer?.Dispose();
            _cisaTimer?.Dispose();
            _aiRatingTimer?.Dispose();
            _cancellationTokenSource.Dispose();
            base.Dispose();
        }
    }
}

