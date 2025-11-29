using MyApp.Namespace.Services;

namespace MyApp.Namespace.Services
{
    public class ApiSourceService
    {
        private readonly DatabaseService _db;

        public ApiSourceService(DatabaseService db)
        {
            _db = db;
        }

        public async Task UpdateSyncStatusAsync(string sourceName, bool success, string? errorMessage, int threatsFetched)
        {
            try
            {
                var now = DateTime.UtcNow;
                var status = success ? "Success" : "Failed";
                var error = errorMessage != null ? errorMessage : (object)DBNull.Value;

                // Match by api_type instead of name (schema uses api_type ENUM: 'OTX', 'NVD', 'CISA')
                // Map source names to api_type values
                var apiType = sourceName switch
                {
                    "OTX" => "OTX",
                    "NVD" => "NVD",
                    "CISA" => "CISA",
                    _ => sourceName
                };

                var sql = @"UPDATE api_sources 
                           SET last_sync_at = @p0, last_sync_status = @p1, last_sync_error = @p2,
                               threats_fetched_count = threats_fetched_count + @p3
                           WHERE api_type = @p4";

                await _db.ExecuteAsync(sql, now, status, error, threatsFetched, apiType);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to update API source status: {ex.Message}", ex);
            }
        }

        public async Task<bool> IsSourceEnabledAsync(string sourceName)
        {
            try
            {
                // Match by api_type instead of name
                var apiType = sourceName switch
                {
                    "OTX" => "OTX",
                    "NVD" => "NVD",
                    "CISA" => "CISA",
                    _ => sourceName
                };
                var sql = "SELECT enabled FROM api_sources WHERE api_type = @p0 LIMIT 1";
                var result = await _db.QueryScalarAsync(sql, apiType);
                return result != null && Convert.ToBoolean(result);
            }
            catch
            {
                return false;
            }
        }

        public async Task SetSourceEnabledAsync(string sourceName, bool enabled)
        {
            try
            {
                // Match by api_type instead of name
                var apiType = sourceName switch
                {
                    "OTX" => "OTX",
                    "NVD" => "NVD",
                    "CISA" => "CISA",
                    _ => sourceName
                };
                var sql = "UPDATE api_sources SET enabled = @p0 WHERE api_type = @p1";
                await _db.ExecuteAsync(sql, enabled, apiType);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to update API source enabled status: {ex.Message}", ex);
            }
        }

        public async Task<Dictionary<string, object>?> GetSourceStatusAsync(string sourceName)
        {
            try
            {
                // Match by api_type instead of name
                var apiType = sourceName switch
                {
                    "OTX" => "OTX",
                    "NVD" => "NVD",
                    "CISA" => "CISA",
                    _ => sourceName
                };
                var sql = "SELECT * FROM api_sources WHERE api_type = @p0 LIMIT 1";
                var results = await _db.QueryAsync(sql, apiType);
                return results.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<Dictionary<string, object>>> GetAllSourcesAsync()
        {
            try
            {
                var sql = "SELECT * FROM api_sources ORDER BY name";
                return await _db.QueryAsync(sql);
            }
            catch
            {
                return new List<Dictionary<string, object>>();
            }
        }
    }
}

