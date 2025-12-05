using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;
using System.Linq;
using MySqlConnector;
using api.DataAccess;
using api.Services;
using api.Models;
using System.Text.Json;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly ApiValidationService? _validationService;
        private readonly ThreatIngestionBackgroundService? _ingestionService;
        private readonly ApiSourceService? _apiSourceService;
        private readonly AuthService _authService;
        private readonly IFileStorageService _fileStorage;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            AuthService authService,
            IFileStorageService fileStorage,
            ILogger<AdminController> logger,
            ApiValidationService? validationService = null,
            ThreatIngestionBackgroundService? ingestionService = null,
            ApiSourceService? apiSourceService = null)
        {
            _authService = authService;
            _fileStorage = fileStorage;
            _logger = logger;
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

        // ==================== USER MANAGEMENT ENDPOINTS ====================

        [HttpGet("pending-users")]
        public async Task<IActionResult> GetPendingUsers()
        {
            try
            {
                var users = await _authService.GetPendingUsersAsync();
                
                return Ok(new
                {
                    users = users.Select(u => new
                    {
                        u.Id,
                        u.Email,
                        u.FullName,
                        u.FacilityName,
                        facilityType = u.FacilityType.ToString(),
                        u.CreatedAt,
                        u.AiConfidenceScore,
                        daysWaiting = (DateTime.UtcNow - u.CreatedAt).Days
                    }),
                    total = users.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending users");
                return StatusCode(500, new { message = "An error occurred fetching pending users" });
            }
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserDetails(int userId)
        {
            try
            {
                var user = await _authService.GetUserWithDocumentsAsync(userId);
                
                if (user == null)
                    return NotFound(new { message = "User not found" });

                // Parse AI analysis
                Dictionary<string, object>? aiAnalysis = null;
                if (!string.IsNullOrEmpty(user.AiAnalysisResult))
                {
                    try
                    {
                        aiAnalysis = JsonSerializer.Deserialize<Dictionary<string, object>>(user.AiAnalysisResult);
                    }
                    catch
                    {
                        aiAnalysis = new Dictionary<string, object> { { "error", "Failed to parse AI analysis" } };
                    }
                }

                return Ok(new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.FacilityName,
                    facilityType = user.FacilityType.ToString(),
                    status = user.Status.ToString(),
                    user.CreatedAt,
                    user.AiConfidenceScore,
                    aiAnalysis,
                    user.RequiresManualReview,
                    documents = new
                    {
                        workId = user.WorkIdPath != null,
                        license = user.ProfessionalLicensePath != null,
                        supervisorLetter = user.SupervisorLetterPath != null
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user details");
                return StatusCode(500, new { message = "An error occurred fetching user details" });
            }
        }

        [HttpGet("document/{userId}/{type}")]
        public async Task<IActionResult> GetDocument(int userId, string type)
        {
            try
            {
                var user = await _authService.GetUserWithDocumentsAsync(userId);
                
                if (user == null)
                    return NotFound(new { message = "User not found" });

                string? documentPath = type.ToLower() switch
                {
                    "workid" => user.WorkIdPath,
                    "license" => user.ProfessionalLicensePath,
                    "letter" => user.SupervisorLetterPath,
                    _ => null
                };

                if (string.IsNullOrEmpty(documentPath))
                    return NotFound(new { message = "Document not found" });

                var fileBytes = await _fileStorage.GetDocumentAsync(documentPath);
                var extension = Path.GetExtension(documentPath).ToLowerInvariant();
                
                var contentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => "application/octet-stream"
                };

                return File(fileBytes, contentType);
            }
            catch (FileNotFoundException)
            {
                return NotFound(new { message = "Document file not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching document");
                return StatusCode(500, new { message = "An error occurred fetching document" });
            }
        }

        [HttpPost("user/{userId}/approve")]
        public async Task<IActionResult> ApproveUser(int userId, [FromBody] AdminUserDecisionRequest? request)
        {
            try
            {
                var adminId = GetCurrentUserId();
                var user = await _authService.GetUserByIdAsync(userId);
                
                if (user == null)
                    return NotFound(new { message = "User not found" });

                if (user.Status != UserStatus.Pending)
                    return BadRequest(new { message = "User is not in pending status" });

                await _authService.ApproveUserAsync(userId, adminId);

                // Log audit
                await LogUserAuditAsync(userId, adminId, "User_Approved", $"User {user.Email} approved by admin");

                _logger.LogInformation($"User {user.Email} approved by admin {adminId}");

                return Ok(new { message = "User approved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving user");
                return StatusCode(500, new { message = "An error occurred approving user" });
            }
        }

        [HttpPost("user/{userId}/reject")]
        public async Task<IActionResult> RejectUser(int userId, [FromBody] AdminUserDecisionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Reason))
                    return BadRequest(new { message = "Rejection reason is required" });

                var adminId = GetCurrentUserId();
                var user = await _authService.GetUserByIdAsync(userId);
                
                if (user == null)
                    return NotFound(new { message = "User not found" });

                if (user.Status != UserStatus.Pending)
                    return BadRequest(new { message = "User is not in pending status" });

                await _authService.RejectUserAsync(userId, adminId, request.Reason);

                // Log audit
                await LogUserAuditAsync(userId, adminId, "User_Rejected", $"User {user.Email} rejected: {request.Reason}");

                _logger.LogInformation($"User {user.Email} rejected by admin {adminId}");

                return Ok(new { message = "User rejected" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting user");
                return StatusCode(500, new { message = "An error occurred rejecting user" });
            }
        }

        [HttpGet("user-statistics")]
        public async Task<IActionResult> GetUserStatistics()
        {
            try
            {
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                var stats = new Dictionary<string, int>();

                // Get counts for each status
                var countQuery = @"
                    SELECT status, COUNT(*) as count 
                    FROM users 
                    GROUP BY status";

                using var command = new MySqlConnector.MySqlCommand(countQuery, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var status = reader.GetString("status");
                    var count = reader.GetInt32("count");
                    stats[status.ToLower() + "Count"] = count;
                }
                reader.Close();

                // Get auto-approved count
                var autoApprovedQuery = "SELECT COUNT(*) FROM users WHERE status = 'Active' AND requires_manual_review = 0";
                using var autoCmd = new MySqlConnector.MySqlCommand(autoApprovedQuery, connection);
                var autoApproved = Convert.ToInt32(await autoCmd.ExecuteScalarAsync());

                // Get average confidence score
                var avgScoreQuery = "SELECT AVG(ai_confidence_score) FROM users WHERE ai_confidence_score IS NOT NULL";
                using var avgCmd = new MySqlConnector.MySqlCommand(avgScoreQuery, connection);
                var avgScore = await avgCmd.ExecuteScalarAsync();

                return Ok(new
                {
                    pendingCount = stats.GetValueOrDefault("pendingCount", 0),
                    activeCount = stats.GetValueOrDefault("activeCount", 0),
                    rejectedCount = stats.GetValueOrDefault("rejectedCount", 0),
                    disabledCount = stats.GetValueOrDefault("disabledCount", 0),
                    autoApprovedCount = autoApproved,
                    averageConfidenceScore = avgScore != DBNull.Value ? Convert.ToDouble(avgScore) : 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user statistics");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        private async Task LogUserAuditAsync(int userId, int adminId, string actionType, string details)
        {
            try
            {
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                var query = "INSERT INTO audit_logs (user_id, action_type, action_details, timestamp) VALUES (@user_id, @action_type, @action_details, NOW())";
                using var command = new MySqlConnector.MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@user_id", adminId);
                command.Parameters.AddWithValue("@action_type", actionType);
                command.Parameters.AddWithValue("@action_details", details);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log user audit entry");
            }
        }

        // ==================== ACTIVE USER MANAGEMENT ENDPOINTS ====================

        [HttpGet("users")]
        public async Task<IActionResult> GetActiveUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null)
        {
            try
            {
                var db = new api.DataAccess.Database();
                var connectionString = db.connectionString;

                using var connection = new MySqlConnector.MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Build query with optional search - show all users except Pending/Rejected
                // Include NULL status (legacy users) and Active users
                var whereClause = "WHERE (status IS NULL OR status = 'Active' OR status NOT IN ('Pending', 'Rejected', 'Disabled'))";
                if (!string.IsNullOrWhiteSpace(search))
                {
                    whereClause += " AND (email LIKE @search OR COALESCE(full_name, '') LIKE @search)";
                }

                // Get total count
                var countQuery = $"SELECT COUNT(*) FROM users {whereClause}";
                using var countCmd = new MySqlConnector.MySqlCommand(countQuery, connection);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    countCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                // Get paginated users with NULL handling
                var offset = (page - 1) * pageSize;
                var selectQuery = $@"
                    SELECT id, 
                           COALESCE(email, '') as email, 
                           COALESCE(full_name, '') as full_name, 
                           COALESCE(facility_name, '') as facility_name, 
                           COALESCE(facility_type, 'Hospital') as facility_type, 
                           COALESCE(role, 'User') as role, 
                           COALESCE(status, 'Active') as status, 
                           COALESCE(created_at, NOW()) as created_at, 
                           COALESCE(is_two_factor_enabled, 0) as is_two_factor_enabled
                    FROM users 
                    {whereClause}
                    ORDER BY created_at DESC
                    LIMIT @limit OFFSET @offset";

                using var selectCmd = new MySqlConnector.MySqlCommand(selectQuery, connection);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    selectCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                selectCmd.Parameters.AddWithValue("@limit", pageSize);
                selectCmd.Parameters.AddWithValue("@offset", offset);

                using var reader = await selectCmd.ExecuteReaderAsync();
                var users = new List<object>();
                
                while (await reader.ReadAsync())
                {
                    var role = reader.GetString("role");
                    bool twoFaEnabled = reader.GetBoolean("is_two_factor_enabled");
                    
                    users.Add(new
                    {
                        id = reader.GetInt32("id"),
                        email = reader.GetString("email"),
                        fullName = reader.GetString("full_name"),
                        facilityName = reader.GetString("facility_name"),
                        facilityType = reader.GetString("facility_type"),
                        role = role,
                        isAdmin = role == "Admin",
                        status = reader.GetString("status"),
                        createdAt = reader.GetDateTime("created_at"),
                        twoFactorEnabled = twoFaEnabled
                    });
                }

                return Ok(new
                {
                    users,
                    total,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active users: {Message}", ex.Message);
                return StatusCode(500, new { message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost("users/{userId}/promote")]
        public async Task<IActionResult> PromoteUser(int userId)
        {
            try
            {
                var adminId = GetCurrentUserId();
                
                // Prevent self-promotion
                if (userId == adminId)
                    return BadRequest(new { message = "Cannot modify your own role" });

                var user = await _authService.GetUserByIdAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                if (user.Role == UserRole.Admin)
                    return BadRequest(new { message = "User is already an admin" });

                // Update role in database
                var db = new api.DataAccess.Database();
                using var connection = new MySqlConnector.MySqlConnection(db.connectionString);
                await connection.OpenAsync();

                var updateQuery = "UPDATE users SET role = 'Admin' WHERE id = @id";
                using var cmd = new MySqlConnector.MySqlCommand(updateQuery, connection);
                cmd.Parameters.AddWithValue("@id", userId);
                await cmd.ExecuteNonQueryAsync();

                await LogUserAuditAsync(userId, adminId, "User_Promoted", $"User {user.Email} promoted to Admin");
                _logger.LogInformation($"User {user.Email} promoted to Admin by admin {adminId}");

                return Ok(new { message = $"{user.Email} promoted to Admin" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error promoting user");
                return StatusCode(500, new { message = "An error occurred promoting user" });
            }
        }

        [HttpPost("users/{userId}/demote")]
        public async Task<IActionResult> DemoteUser(int userId)
        {
            try
            {
                var adminId = GetCurrentUserId();
                
                // Prevent self-demotion
                if (userId == adminId)
                    return BadRequest(new { message = "Cannot modify your own role" });

                var user = await _authService.GetUserByIdAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                if (user.Role != UserRole.Admin)
                    return BadRequest(new { message = "User is not an admin" });

                // Check if this is the last admin
                var db = new api.DataAccess.Database();
                using var connection = new MySqlConnector.MySqlConnection(db.connectionString);
                await connection.OpenAsync();

                var countQuery = "SELECT COUNT(*) FROM users WHERE role = 'Admin' AND status = 'Active'";
                using var countCmd = new MySqlConnector.MySqlCommand(countQuery, connection);
                var adminCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                if (adminCount <= 1)
                    return BadRequest(new { message = "Cannot demote the last admin" });

                // Update role in database
                var updateQuery = "UPDATE users SET role = 'User' WHERE id = @id";
                using var cmd = new MySqlConnector.MySqlCommand(updateQuery, connection);
                cmd.Parameters.AddWithValue("@id", userId);
                await cmd.ExecuteNonQueryAsync();

                await LogUserAuditAsync(userId, adminId, "User_Demoted", $"User {user.Email} demoted from Admin");
                _logger.LogInformation($"User {user.Email} demoted from Admin by admin {adminId}");

                return Ok(new { message = $"{user.Email} demoted to regular user" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error demoting user");
                return StatusCode(500, new { message = "An error occurred demoting user" });
            }
        }

        [HttpPut("users/{userId}")]
        public async Task<IActionResult> EditUser(int userId, [FromBody] EditUserRequest request)
        {
            try
            {
                var adminId = GetCurrentUserId();
                var user = await _authService.GetUserByIdAsync(userId);
                
                if (user == null)
                    return NotFound(new { message = "User not found" });

                var db = new api.DataAccess.Database();
                using var connection = new MySqlConnector.MySqlConnection(db.connectionString);
                await connection.OpenAsync();

                // Check if email is changing and if it's already in use
                if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
                {
                    var checkQuery = "SELECT COUNT(*) FROM users WHERE email = @email AND id != @id";
                    using var checkCmd = new MySqlConnector.MySqlCommand(checkQuery, connection);
                    checkCmd.Parameters.AddWithValue("@email", request.Email);
                    checkCmd.Parameters.AddWithValue("@id", userId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
                    
                    if (exists)
                        return BadRequest(new { message = "Email already in use" });
                }

                // Build update query
                var updates = new List<string>();
                var parameters = new List<MySqlConnector.MySqlParameter>();

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    updates.Add("email = @email");
                    parameters.Add(new MySqlConnector.MySqlParameter("@email", request.Email));
                }
                if (!string.IsNullOrWhiteSpace(request.FullName))
                {
                    updates.Add("full_name = @fullName");
                    parameters.Add(new MySqlConnector.MySqlParameter("@fullName", request.FullName));
                }
                if (!string.IsNullOrWhiteSpace(request.FacilityName))
                {
                    updates.Add("facility_name = @facilityName");
                    parameters.Add(new MySqlConnector.MySqlParameter("@facilityName", request.FacilityName));
                }

                if (updates.Count == 0)
                    return BadRequest(new { message = "No fields to update" });

                var updateQuery = $"UPDATE users SET {string.Join(", ", updates)} WHERE id = @id";
                using var cmd = new MySqlConnector.MySqlCommand(updateQuery, connection);
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(param);
                }
                cmd.Parameters.AddWithValue("@id", userId);
                await cmd.ExecuteNonQueryAsync();

                await LogUserAuditAsync(userId, adminId, "User_Edited", $"User {user.Email} details updated");
                _logger.LogInformation($"User {user.Email} edited by admin {adminId}");

                return Ok(new { message = "User updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error editing user");
                return StatusCode(500, new { message = "An error occurred editing user" });
            }
        }

        [HttpDelete("users/{userId}")]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            try
            {
                var adminId = GetCurrentUserId();
                
                // Prevent self-deletion
                if (userId == adminId)
                    return BadRequest(new { message = "Cannot delete your own account" });

                var user = await _authService.GetUserWithDocumentsAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                // Check if deleting the last admin
                if (user.Role == UserRole.Admin)
                {
                    var db2 = new api.DataAccess.Database();
                    using var conn = new MySqlConnector.MySqlConnection(db2.connectionString);
                    await conn.OpenAsync();

                    var countQuery = "SELECT COUNT(*) FROM users WHERE role = 'Admin' AND status = 'Active'";
                    using var countCmd = new MySqlConnector.MySqlCommand(countQuery, conn);
                    var adminCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                    if (adminCount <= 1)
                        return BadRequest(new { message = "Cannot delete the last admin" });
                }

                // Delete associated documents
                try
                {
                    if (!string.IsNullOrEmpty(user.WorkIdPath))
                        await _fileStorage.DeleteDocumentAsync(user.WorkIdPath);
                    if (!string.IsNullOrEmpty(user.ProfessionalLicensePath))
                        await _fileStorage.DeleteDocumentAsync(user.ProfessionalLicensePath);
                    if (!string.IsNullOrEmpty(user.SupervisorLetterPath))
                        await _fileStorage.DeleteDocumentAsync(user.SupervisorLetterPath);
                }
                catch (Exception docEx)
                {
                    _logger.LogWarning($"Failed to delete documents for user {user.Email}: {docEx.Message}");
                }

                // Delete user from database
                var db = new api.DataAccess.Database();
                using var connection = new MySqlConnector.MySqlConnection(db.connectionString);
                await connection.OpenAsync();

                var deleteQuery = "DELETE FROM users WHERE id = @id";
                using var cmd = new MySqlConnector.MySqlCommand(deleteQuery, connection);
                cmd.Parameters.AddWithValue("@id", userId);
                await cmd.ExecuteNonQueryAsync();

                await LogUserAuditAsync(userId, adminId, "User_Deleted", $"User {user.Email} deleted");
                _logger.LogInformation($"User {user.Email} deleted by admin {adminId}");

                return Ok(new { message = $"User {user.Email} deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user");
                return StatusCode(500, new { message = "An error occurred deleting user" });
            }
        }

        // ==================== THREAT MANAGEMENT ENDPOINTS ====================

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

public class AdminUserDecisionRequest
{
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}

public class EditUserRequest
{
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? FacilityName { get; set; }
}
