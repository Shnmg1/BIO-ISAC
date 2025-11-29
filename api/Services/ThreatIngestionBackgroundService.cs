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
            _cancellationTokenSource.Cancel();
            _logger.LogInformation("Threat Ingestion Background Service stopped");
        }

        public override void Dispose()
        {
            _otxTimer?.Dispose();
            _nvdTimer?.Dispose();
            _cisaTimer?.Dispose();
            _cancellationTokenSource.Dispose();
            base.Dispose();
        }
    }
}

