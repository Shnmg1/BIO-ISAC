using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;
using System.Linq;
using MySqlConnector;
using api.DataAccess;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly ApiValidationService? _validationService;
        private readonly ThreatIngestionBackgroundService? _ingestionService;
        private readonly ApiSourceService? _apiSourceService;

        public AdminController(
            ApiValidationService? validationService = null,
            ThreatIngestionBackgroundService? ingestionService = null,
            ApiSourceService? apiSourceService = null)
        {
            _validationService = validationService;
            _ingestionService = ingestionService;
            _apiSourceService = apiSourceService;
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new { 
                message = "AdminController is working", 
                timestamp = DateTime.UtcNow,
                route = "api/admin/test"
            });
        }

        [HttpGet("check-database-tables")]
        public async Task<IActionResult> CheckDatabaseTables()
        {
            try
            {
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                var tablesToCheck = new[] { "threats", "api_sources", "audit_logs" };
                var results = new Dictionary<string, object>();

                foreach (var tableName in tablesToCheck)
                {
                    var checkSql = @"
                        SELECT COUNT(*) as count 
                        FROM information_schema.tables 
                        WHERE table_schema = DATABASE() 
                        AND table_name = @tableName";

                    using var command = new MySqlConnector.MySqlCommand(checkSql, connection);
                    command.Parameters.AddWithValue("@tableName", tableName);

                    var result = await command.ExecuteScalarAsync();
                    var exists = result != null && Convert.ToInt32(result) > 0;

                    var tableInfo = new Dictionary<string, object>
                    {
                        { "exists", exists }
                    };

                    if (exists)
                    {
                        // Get column information
                        var columnsSql = @"
                            SELECT column_name, data_type, is_nullable, column_default
                            FROM information_schema.columns
                            WHERE table_schema = DATABASE()
                            AND table_name = @tableName
                            ORDER BY ordinal_position";

                        using var columnsCommand = new MySqlConnector.MySqlCommand(columnsSql, connection);
                        columnsCommand.Parameters.AddWithValue("@tableName", tableName);

                        using var reader = await columnsCommand.ExecuteReaderAsync();
                        var columns = new List<object>();
                        while (await reader.ReadAsync())
                        {
                            var defaultValueObj = reader["column_default"];
                            var defaultValue = (defaultValueObj == null || defaultValueObj == DBNull.Value) 
                                ? null 
                                : defaultValueObj.ToString();
                            
                            columns.Add(new
                            {
                                name = reader.GetString("column_name"),
                                type = reader.GetString("data_type"),
                                nullable = reader.GetString("is_nullable") == "YES",
                                defaultValue = defaultValue
                            });
                        }
                        reader.Close();
                        tableInfo["columns"] = columns;

                        // Get row count
                        var countSql = $"SELECT COUNT(*) FROM {tableName}";
                        using var countCommand = new MySqlConnector.MySqlCommand(countSql, connection);
                        var rowCount = await countCommand.ExecuteScalarAsync();
                        tableInfo["rowCount"] = Convert.ToInt32(rowCount);
                    }

                    results[tableName] = tableInfo;
                }

                // Get all tables
                var allTablesSql = @"
                    SELECT table_name 
                    FROM information_schema.tables 
                    WHERE table_schema = DATABASE()
                    ORDER BY table_name";

                using var allTablesCommand = new MySqlConnector.MySqlCommand(allTablesSql, connection);
                using var tablesReader = await allTablesCommand.ExecuteReaderAsync();

                var allTables = new List<string>();
                while (await tablesReader.ReadAsync())
                {
                    allTables.Add(tablesReader.GetString("table_name"));
                }

                return Ok(new
                {
                    tables = results,
                    allTables = allTables,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("validate-apis")]
        public async Task<IActionResult> ValidateAPIs()
        {
            if (_validationService == null)
            {
                return StatusCode(500, new { error = "ApiValidationService is not available. Check service registration." });
            }

            try
            {
                var report = await _validationService.TestAllAPIs();
                return Ok(report);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpPost("threat-ingestion/start")]
        public async Task<IActionResult> StartThreatIngestion()
        {
            if (_ingestionService == null)
            {
                return StatusCode(500, new { error = "ThreatIngestionBackgroundService is not available." });
            }

            try
            {
                await _ingestionService.SyncAllAsync();
                return Ok(new { 
                    message = "Sync started", 
                    apis = new[] { "OTX", "NVD", "CISA" },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("threat-ingestion/stop")]
        public IActionResult StopThreatIngestion()
        {
            if (_ingestionService == null)
            {
                return StatusCode(500, new { error = "ThreatIngestionBackgroundService is not available." });
            }

            try
            {
                _ingestionService.Stop();
                return Ok(new { message = "Background sync stopped", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("threat-ingestion/status")]
        public async Task<IActionResult> GetThreatIngestionStatus()
        {
            if (_apiSourceService == null)
            {
                return StatusCode(500, new { error = "ApiSourceService is not available." });
            }

            try
            {
                var sources = await _apiSourceService.GetAllSourcesAsync();
                var apis = new List<object>();

                foreach (var source in sources)
                {
                    apis.Add(new
                    {
                        name = source.TryGetValue("name", out var n) ? n.ToString() : "",
                        enabled = source.TryGetValue("enabled", out var e) ? Convert.ToBoolean(e) : false,
                        lastSyncAt = source.TryGetValue("last_sync_at", out var lsa) && lsa != DBNull.Value ? lsa.ToString() : null,
                        lastSyncStatus = source.TryGetValue("last_sync_status", out var lss) ? lss.ToString() : null,
                        lastSyncError = source.TryGetValue("last_sync_error", out var lse) && lse != DBNull.Value ? lse.ToString() : null,
                        threatsFetchedCount = source.TryGetValue("threats_fetched_count", out var tfc) ? Convert.ToInt32(tfc) : 0
                    });
                }

                return Ok(new
                {
                    isRunning = true,
                    lastSync = apis.Select(a => ((dynamic)a).lastSyncAt).FirstOrDefault(s => s != null),
                    apis = apis,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("api-sources/{id}/toggle")]
        public async Task<IActionResult> ToggleApiSource(string id)
        {
            if (_apiSourceService == null)
            {
                return StatusCode(500, new { error = "ApiSourceService is not available." });
            }

            try
            {
                // Accept either api_type (OTX, NVD, CISA) or name
                var currentStatus = await _apiSourceService.GetSourceStatusAsync(id);
                if (currentStatus == null)
                {
                    return NotFound(new { error = $"API source '{id}' not found. Use 'OTX', 'NVD', or 'CISA'" });
                }

                var currentEnabled = currentStatus.TryGetValue("enabled", out var e) ? Convert.ToBoolean(e) : false;
                var newEnabled = !currentEnabled;

                await _apiSourceService.SetSourceEnabledAsync(id, newEnabled);

                return Ok(new
                {
                    message = "API source updated",
                    enabled = newEnabled,
                    source = id,
                    apiType = currentStatus.TryGetValue("api_type", out var at) ? at.ToString() : id,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private int GetCurrentUserId()
        {
            if (Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) && 
                int.TryParse(userIdHeader, out var userId))
            {
                return userId;
            }
            return 1;
        }

        [HttpPost("threats/{id}/approve")]
        public async Task<IActionResult> ApproveThreat(int id, [FromBody] ApproveThreatRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Get threat to check if it exists
                var threatQuery = "SELECT id, status FROM threats WHERE id = @id";
                using (var threatCmd = new MySqlConnector.MySqlCommand(threatQuery, connection))
                {
                    threatCmd.Parameters.AddWithValue("@id", id);
                    var threatResult = await threatCmd.ExecuteScalarAsync();
                    if (threatResult == null)
                    {
                        return NotFound(new { message = "Threat not found" });
                    }
                }

                // Update threat status
                var updateThreatQuery = "UPDATE threats SET status = 'Approved' WHERE id = @id";
                using (var updateCmd = new MySqlConnector.MySqlCommand(updateThreatQuery, connection))
                {
                    updateCmd.Parameters.AddWithValue("@id", id);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Update or create threat_analysis record
                var checkClassQuery = "SELECT id FROM threat_analysis WHERE threat_id = @threat_id";
                int? analysisId = null;
                using (var checkCmd = new MySqlConnector.MySqlCommand(checkClassQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@threat_id", id);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        analysisId = Convert.ToInt32(result);
                    }
                }

                if (analysisId.HasValue)
                {
                    var updateClassQuery = @"
                        UPDATE threat_analysis 
                        SET human_decision = 'Approved',
                            human_justification = @justification,
                            reviewed_by = @reviewed_by,
                            reviewed_at = NOW()
                        WHERE id = @id";
                    using (var updateCmd = new MySqlConnector.MySqlCommand(updateClassQuery, connection))
                    {
                        updateCmd.Parameters.AddWithValue("@id", analysisId.Value);
                        updateCmd.Parameters.AddWithValue("@justification", request.Justification ?? "Approved by admin");
                        updateCmd.Parameters.AddWithValue("@reviewed_by", userId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    var insertClassQuery = @"
                        INSERT INTO threat_analysis (threat_id, human_decision, human_justification, reviewed_by, reviewed_at, created_at)
                        VALUES (@threat_id, 'Approved', @justification, @reviewed_by, NOW(), NOW())";
                    using (var insertCmd = new MySqlConnector.MySqlCommand(insertClassQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@threat_id", id);
                        insertCmd.Parameters.AddWithValue("@justification", request.Justification ?? "Approved by admin");
                        insertCmd.Parameters.AddWithValue("@reviewed_by", userId);
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }

                // Log audit
                var auditQuery = "INSERT INTO audit_logs (threat_id, user_id, action_type, action_details, timestamp) VALUES (@threat_id, @user_id, 'Threat_Approved', @details, NOW())";
                using (var auditCmd = new MySqlConnector.MySqlCommand(auditQuery, connection))
                {
                    auditCmd.Parameters.AddWithValue("@threat_id", id);
                    auditCmd.Parameters.AddWithValue("@user_id", userId);
                    auditCmd.Parameters.AddWithValue("@details", $"Threat {id} approved: {request.Justification ?? "No justification provided"}");
                    await auditCmd.ExecuteNonQueryAsync();
                }

                return Ok(new { message = "Threat approved successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while approving the threat", error = ex.Message });
            }
        }

        [HttpPost("threats/{id}/reject")]
        public async Task<IActionResult> RejectThreat(int id, [FromBody] RejectThreatRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Get threat to check if it exists
                var threatQuery = "SELECT id, status FROM threats WHERE id = @id";
                using (var threatCmd = new MySqlConnector.MySqlCommand(threatQuery, connection))
                {
                    threatCmd.Parameters.AddWithValue("@id", id);
                    var threatResult = await threatCmd.ExecuteScalarAsync();
                    if (threatResult == null)
                    {
                        return NotFound(new { message = "Threat not found" });
                    }
                }

                // Update threat status
                var updateThreatQuery = "UPDATE threats SET status = 'Rejected' WHERE id = @id";
                using (var updateCmd = new MySqlConnector.MySqlCommand(updateThreatQuery, connection))
                {
                    updateCmd.Parameters.AddWithValue("@id", id);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Update or create threat_analysis record
                var checkClassQuery = "SELECT id FROM threat_analysis WHERE threat_id = @threat_id";
                int? analysisId = null;
                using (var checkCmd = new MySqlConnector.MySqlCommand(checkClassQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@threat_id", id);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        analysisId = Convert.ToInt32(result);
                    }
                }

                if (analysisId.HasValue)
                {
                    var updateClassQuery = @"
                        UPDATE threat_analysis 
                        SET human_decision = 'False_Positive',
                            human_justification = @justification,
                            reviewed_by = @reviewed_by,
                            reviewed_at = NOW()
                        WHERE id = @id";
                    using (var updateCmd = new MySqlConnector.MySqlCommand(updateClassQuery, connection))
                    {
                        updateCmd.Parameters.AddWithValue("@id", analysisId.Value);
                        updateCmd.Parameters.AddWithValue("@justification", request.Justification ?? "Rejected by admin");
                        updateCmd.Parameters.AddWithValue("@reviewed_by", userId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    var insertClassQuery = @"
                        INSERT INTO threat_analysis (threat_id, human_decision, human_justification, reviewed_by, reviewed_at, created_at)
                        VALUES (@threat_id, 'False_Positive', @justification, @reviewed_by, NOW(), NOW())";
                    using (var insertCmd = new MySqlConnector.MySqlCommand(insertClassQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@threat_id", id);
                        insertCmd.Parameters.AddWithValue("@justification", request.Justification ?? "Rejected by admin");
                        insertCmd.Parameters.AddWithValue("@reviewed_by", userId);
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }

                // Log audit
                var auditQuery = "INSERT INTO audit_logs (threat_id, user_id, action_type, action_details, timestamp) VALUES (@threat_id, @user_id, 'Threat_Rejected', @details, NOW())";
                using (var auditCmd = new MySqlConnector.MySqlCommand(auditQuery, connection))
                {
                    auditCmd.Parameters.AddWithValue("@threat_id", id);
                    auditCmd.Parameters.AddWithValue("@user_id", userId);
                    auditCmd.Parameters.AddWithValue("@details", $"Threat {id} rejected: {request.Justification ?? "No justification provided"}");
                    await auditCmd.ExecuteNonQueryAsync();
                }

                return Ok(new { message = "Threat rejected successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while rejecting the threat", error = ex.Message });
            }
        }

        [HttpPost("threats/{id}/override")]
        public async Task<IActionResult> OverrideThreat(int id, [FromBody] OverrideThreatRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Get threat to check if it exists
                var threatQuery = "SELECT id, status FROM threats WHERE id = @id";
                using (var threatCmd = new MySqlConnector.MySqlCommand(threatQuery, connection))
                {
                    threatCmd.Parameters.AddWithValue("@id", id);
                    var threatResult = await threatCmd.ExecuteScalarAsync();
                    if (threatResult == null)
                    {
                        return NotFound(new { message = "Threat not found" });
                    }
                }

                // Update threat status
                var updateThreatQuery = "UPDATE threats SET status = 'Approved' WHERE id = @id";
                using (var updateCmd = new MySqlConnector.MySqlCommand(updateThreatQuery, connection))
                {
                    updateCmd.Parameters.AddWithValue("@id", id);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Update or create threat_analysis record with override
                var checkClassQuery = "SELECT id FROM threat_analysis WHERE threat_id = @threat_id";
                int? analysisId = null;
                using (var checkCmd = new MySqlConnector.MySqlCommand(checkClassQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@threat_id", id);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        analysisId = Convert.ToInt32(result);
                    }
                }

                if (analysisId.HasValue)
                {
                    var updateClassQuery = @"
                        UPDATE threat_analysis 
                        SET human_tier = @human_tier,
                            human_decision = 'Override',
                            human_justification = @justification,
                            reviewed_by = @reviewed_by,
                            reviewed_at = NOW()
                        WHERE id = @id";
                    using (var updateCmd = new MySqlConnector.MySqlCommand(updateClassQuery, connection))
                    {
                        updateCmd.Parameters.AddWithValue("@id", analysisId.Value);
                        updateCmd.Parameters.AddWithValue("@human_tier", request.Tier ?? "Medium");
                        updateCmd.Parameters.AddWithValue("@justification", request.Justification ?? "Overridden by admin");
                        updateCmd.Parameters.AddWithValue("@reviewed_by", userId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    var insertClassQuery = @"
                        INSERT INTO threat_analysis (threat_id, human_tier, human_decision, human_justification, reviewed_by, reviewed_at, created_at)
                        VALUES (@threat_id, @human_tier, 'Override', @justification, @reviewed_by, NOW(), NOW())";
                    using (var insertCmd = new MySqlConnector.MySqlCommand(insertClassQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@threat_id", id);
                        insertCmd.Parameters.AddWithValue("@human_tier", request.Tier ?? "Medium");
                        insertCmd.Parameters.AddWithValue("@justification", request.Justification ?? "Overridden by admin");
                        insertCmd.Parameters.AddWithValue("@reviewed_by", userId);
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }

                // Log audit
                var auditQuery = "INSERT INTO audit_logs (threat_id, user_id, action_type, action_details, timestamp) VALUES (@threat_id, @user_id, 'Threat_Overridden', @details, NOW())";
                using (var auditCmd = new MySqlConnector.MySqlCommand(auditQuery, connection))
                {
                    auditCmd.Parameters.AddWithValue("@threat_id", id);
                    auditCmd.Parameters.AddWithValue("@user_id", userId);
                    auditCmd.Parameters.AddWithValue("@details", $"Threat {id} overridden with tier {request.Tier}: {request.Justification ?? "No justification provided"}");
                    await auditCmd.ExecuteNonQueryAsync();
                }

                return Ok(new { message = "Threat overridden successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while overriding the threat", error = ex.Message });
            }
        }
    }
}

public class ApproveThreatRequest
{
    public string? Justification { get; set; }
}

public class RejectThreatRequest
{
    public string? Justification { get; set; }
}

public class OverrideThreatRequest
{
    public string? Tier { get; set; }
    public string? Justification { get; set; }
}
