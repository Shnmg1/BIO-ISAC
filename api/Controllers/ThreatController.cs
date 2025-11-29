using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Models;
using api.Services;
using MyApp.Namespace.Services;
using api.Extensions;
using api.DTOs;
using MySqlConnector;

namespace api.Controllers;

[ApiController]
[Route("api/threats")]
public class ThreatController : ControllerBase
{
    private readonly DatabaseService _dbService;
    private readonly AIService _aiService;
    private readonly ILogger<ThreatController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly AuthService _authService;

    public ThreatController(DatabaseService dbService, AIService aiService, ILogger<ThreatController> logger, IWebHostEnvironment environment, AuthService authService)
    {
        _dbService = dbService;
        _aiService = aiService;
        _logger = logger;
        _environment = environment;
        _authService = authService;
    }

    private int GetCurrentUserId()
    {
        // Get user ID from request header if provided, otherwise use default system user
        if (Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) && 
            int.TryParse(userIdHeader, out var userId))
        {
            return userId;
        }
        // Default to system user (ID 1) if no user ID provided
        return 1;
    }

    [HttpPost]
    public async Task<IActionResult> CreateThreat([FromBody] ThreatSubmission request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            // Validation - Required fields
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { message = "Title is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest(new { message = "Description is required" });
            }

            // Additional validation
            if (request.Title.Length > 500)
            {
                return BadRequest(new { message = "Title must be 500 characters or less" });
            }

            if (request.Description.Length > 10000)
            {
                return BadRequest(new { message = "Description must be 10,000 characters or less" });
            }

            // Save threat to database
            int threatId;
            try
            {
                using var connection = await _dbService.GetConnectionAsync();
                var query = @"INSERT INTO threats (user_id, title, description, category, source, date_observed, impact_level, status, created_at) 
                             VALUES (@user_id, @title, @description, @category, @source, @date_observed, @impact_level, 'Pending_AI', NOW())";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@user_id", userId);
                command.Parameters.AddWithValue("@title", request.Title.Trim());
                command.Parameters.AddWithValue("@description", request.Description.Trim());
                command.Parameters.AddWithValue("@category", request.Category ?? "Other");
                command.Parameters.AddWithValue("@source", request.Source ?? "Direct observation");
                command.Parameters.AddWithValue("@date_observed", request.DateObserved ?? DateTime.UtcNow);
                command.Parameters.AddWithValue("@impact_level", request.ImpactLevel ?? "Unknown");

                await command.ExecuteNonQueryAsync();
                threatId = (int)command.LastInsertedId;
            }
            catch (MySqlException dbEx)
            {
                _logger.LogError(dbEx, "Database error creating threat");
                return StatusCode(500, new { message = "Database error occurred while submitting the threat" });
            }

            // Log audit
            await LogAuditAsync(userId, threatId, "Threat_Submitted", $"Threat submitted: {request.Title}");

            // Trigger AI classification asynchronously (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000); // Small delay to ensure threat is committed
                    
                    var threat = await GetThreatByIdAsync(threatId);
                    if (threat != null)
                    {
                        var classification = await _aiService.ClassifyThreatAsync(threat);
                        await _aiService.SaveClassificationAsync(threatId, classification);
                        
                        // Update threat status to Pending_Review
                        await UpdateThreatStatusAsync(threatId, "Pending_Review");
                        
                        _logger.LogInformation($"AI classification completed for threat {threatId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in async AI classification for threat {threatId}");
                    // Update status to indicate AI classification failed
                    await UpdateThreatStatusAsync(threatId, "Pending_Review");
                }
            });

            return Ok(new
            {
                id = threatId,
                message = "Threat submitted successfully",
                status = "Pending_AI"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating threat");
            return StatusCode(500, new { message = "An error occurred while submitting the threat" });
        }
    }

    [HttpGet("user/submitted")]
    public async Task<IActionResult> GetUserSubmittedThreats([FromQuery] string? status = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT t.id, t.user_id, t.title, t.description, t.category, t.source, t.date_observed, 
                                 t.impact_level, t.status, t.created_at
                          FROM threats t
                          WHERE t.user_id = @user_id";
            
            if (!string.IsNullOrEmpty(status))
            {
                query += " AND t.status = @status";
            }
            
            query += " ORDER BY t.created_at DESC";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@user_id", userId);
            if (!string.IsNullOrEmpty(status))
            {
                command.Parameters.AddWithValue("@status", status);
            }

            var threats = new List<object>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                threats.Add(new
                {
                    id = reader.GetInt32ByName("id"),
                    title = reader.GetStringByName("title"),
                    description = reader.GetStringByName("description"),
                    category = reader.GetStringByName("category"),
                    source = reader.GetStringByName("source"),
                    dateObserved = reader.GetDateTimeByName("date_observed"),
                    impactLevel = reader.GetStringByName("impact_level"),
                    status = reader.GetStringByName("status"),
                    createdAt = reader.GetDateTimeByName("created_at")
                });
            }

            return Ok(threats);
        }
        catch (MySqlException dbEx)
        {
            _logger.LogError(dbEx, "Database error fetching threats");
            return StatusCode(500, new { message = "Database error occurred while fetching threats" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching threats");
            return StatusCode(500, new { message = "An error occurred while fetching threats" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetThreat(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            // Check if user is admin - admins can view any threat
            bool isAdmin = false;
            try
            {
                var user = await _authService.GetUserByIdAsync(userId);
                if (user != null)
                {
                    var roleStr = user.Role.ToString();
                    isAdmin = roleStr.Equals("Admin", StringComparison.OrdinalIgnoreCase);
                    _logger.LogInformation("User {UserId} role: {Role}, isAdmin: {IsAdmin}", userId, roleStr, isAdmin);
                }
                else
                {
                    _logger.LogWarning("User {UserId} not found in database", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify admin status for user {UserId}", userId);
            }

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT t.id, t.user_id, t.title, t.description, t.category, t.source, t.date_observed, 
                                 t.impact_level, t.status, t.created_at,
                                 c.id as classification_id, c.ai_tier, c.ai_confidence, c.ai_reasoning, c.ai_actions,
                                 c.human_tier, c.human_decision, c.human_justification, c.reviewed_by, c.reviewed_at
                          FROM threats t
                          LEFT JOIN classifications c ON t.id = c.threat_id
                          WHERE t.id = @id" + (isAdmin ? "" : " AND t.user_id = @user_id");
            
            _logger.LogInformation("Querying threat {ThreatId} for user {UserId} (isAdmin: {IsAdmin})", id, userId, isAdmin);

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", id);
            if (!isAdmin)
            {
                command.Parameters.AddWithValue("@user_id", userId);
            }

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var threat = new
                {
                    id = reader.GetInt32ByName("id"),
                    title = reader.GetStringByName("title"),
                    description = reader.GetStringByName("description"),
                    category = reader.GetStringByName("category"),
                    source = reader.GetStringByName("source"),
                    dateObserved = reader.GetDateTimeByName("date_observed"),
                    impactLevel = reader.GetStringByName("impact_level"),
                    status = reader.GetStringByName("status"),
                    createdAt = reader.GetDateTimeByName("created_at"),
                    classification = reader.IsDBNullByName("classification_id") ? null : new
                    {
                        id = reader.GetInt32ByName("classification_id"),
                        aiTier = reader.IsDBNullByName("ai_tier") ? null : reader.GetStringByName("ai_tier"),
                        aiConfidence = reader.IsDBNullByName("ai_confidence") ? (decimal?)null : reader.GetDecimalByName("ai_confidence"),
                        aiReasoning = reader.IsDBNullByName("ai_reasoning") ? null : reader.GetStringByName("ai_reasoning"),
                        aiActions = reader.IsDBNullByName("ai_actions") ? null : reader.GetStringByName("ai_actions"),
                        humanTier = reader.IsDBNullByName("human_tier") ? null : reader.GetStringByName("human_tier"),
                        humanDecision = reader.IsDBNullByName("human_decision") ? null : reader.GetStringByName("human_decision"),
                        humanJustification = reader.IsDBNullByName("human_justification") ? null : reader.GetStringByName("human_justification"),
                        reviewedBy = reader.IsDBNullByName("reviewed_by") ? (int?)null : reader.GetInt32ByName("reviewed_by"),
                        reviewedAt = reader.IsDBNullByName("reviewed_at") ? (DateTime?)null : reader.GetDateTimeByName("reviewed_at")
                    }
                };

                _logger.LogInformation("Successfully retrieved threat {ThreatId}", id);
                return Ok(threat);
            }

            _logger.LogWarning("Threat {ThreatId} not found for user {UserId} (isAdmin: {IsAdmin})", id, userId, isAdmin);
            return NotFound(new { message = "Threat not found" });
        }
        catch (MySqlException dbEx)
        {
            _logger.LogError(dbEx, "Database error fetching threat");
            return StatusCode(500, new { message = "Database error occurred while fetching the threat" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching threat");
            return StatusCode(500, new { message = "An error occurred while fetching the threat" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateThreat(int id, [FromBody] ThreatSubmission request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            // Validation
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { message = "Title is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest(new { message = "Description is required" });
            }

            // Check if user is admin - admins can update any threat
            bool isAdmin = false;
            try
            {
                var user = await _authService.GetUserByIdAsync(userId);
                if (user != null)
                {
                    var roleStr = user.Role.ToString();
                    isAdmin = roleStr.Equals("Admin", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify admin status for user {UserId}", userId);
            }

            using var connection = await _dbService.GetConnectionAsync();
            
            // Check if threat exists and belongs to user (or user is admin)
            var checkQuery = isAdmin 
                ? "SELECT id FROM threats WHERE id = @id"
                : "SELECT id FROM threats WHERE id = @id AND user_id = @user_id";
            using (var checkCommand = new MySqlCommand(checkQuery, connection))
            {
                checkCommand.Parameters.AddWithValue("@id", id);
                if (!isAdmin)
                {
                    checkCommand.Parameters.AddWithValue("@user_id", userId);
                }
                var result = await checkCommand.ExecuteScalarAsync();
                if (result == null)
                {
                    return NotFound(new { message = "Threat not found or you do not have permission to edit it" });
                }
            }

            // Update threat
            var query = isAdmin
                ? @"UPDATE threats 
                    SET title = @title, 
                        description = @description, 
                        category = @category, 
                        impact_level = @impact_level 
                    WHERE id = @id"
                : @"UPDATE threats 
                    SET title = @title, 
                        description = @description, 
                        category = @category, 
                        impact_level = @impact_level 
                    WHERE id = @id AND user_id = @user_id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@title", request.Title.Trim());
            command.Parameters.AddWithValue("@description", request.Description.Trim());
            command.Parameters.AddWithValue("@category", request.Category ?? "Other");
            command.Parameters.AddWithValue("@impact_level", request.ImpactLevel ?? "Unknown");
            command.Parameters.AddWithValue("@id", id);
            if (!isAdmin)
            {
                command.Parameters.AddWithValue("@user_id", userId);
            }

            await command.ExecuteNonQueryAsync();

            await LogAuditAsync(userId, id, "Threat_Updated", $"Threat updated: {request.Title}");

            return Ok(new { message = "Threat updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating threat {id}");
            return StatusCode(500, new { message = "An error occurred while updating the threat" });
        }
    }

    [HttpGet("user/alerts")]
    public async Task<IActionResult> GetUserAlerts()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized();
            }

            // Get user's facility type
            var user = await GetUserAsync(userId);
            if (user == null)
            {
                return Unauthorized();
            }

            using var connection = await _dbService.GetConnectionAsync();
            // Get approved threats that match user's facility type or are general
            var query = @"SELECT t.id, t.title, t.description, t.category, t.date_observed, t.created_at, 
                                 c.human_tier, c.ai_tier
                          FROM threats t
                          INNER JOIN classifications c ON t.id = c.threat_id
                          WHERE t.status = 'Approved'
                          ORDER BY t.created_at DESC
                          LIMIT 50";

            using var command = new MySqlCommand(query, connection);
            var alerts = new List<object>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                alerts.Add(new
                {
                    id = reader.GetInt32ByName("id"),
                    title = reader.GetStringByName("title"),
                    description = reader.GetStringByName("description"),
                    category = reader.GetStringByName("category"),
                    dateObserved = reader.GetDateTimeByName("date_observed"),
                    tier = reader.IsDBNullByName("human_tier") ? reader.GetStringByName("ai_tier") : reader.GetStringByName("human_tier"),
                    createdAt = reader.GetDateTimeByName("created_at")
                });
            }

            return Ok(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching alerts");
            return StatusCode(500, new { message = "An error occurred while fetching alerts" });
        }
    }

    private async Task<Threat?> GetThreatByIdAsync(int threatId)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = "SELECT id, user_id, title, description, category, source, date_observed, impact_level, status, created_at FROM threats WHERE id = @id";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", threatId);
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Threat
                {
                    id = reader.GetInt32ByName("id"),
                    user_id = reader.GetInt32ByName("user_id"),
                    title = reader.GetStringByName("title"),
                    description = reader.GetStringByName("description"),
                    category = reader.GetStringByName("category"),
                    source = reader.GetStringByName("source"),
                    date_observed = reader.GetDateTimeByName("date_observed"),
                    impact_level = reader.GetStringByName("impact_level"),
                    status = reader.GetStringByName("status"),
                    created_at = reader.GetDateTimeByName("created_at")
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching threat {threatId}");
            return null;
        }
    }

    private async Task UpdateThreatStatusAsync(int threatId, string status)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = "UPDATE threats SET status = @status WHERE id = @id";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@status", status.ToString());
            command.Parameters.AddWithValue("@id", threatId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating threat status for threat {threatId}");
        }
    }

    private async Task<User?> GetUserAsync(int userId)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, full_name, facility_name, facility_type, role, status FROM users WHERE id = @id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", userId);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name"),
                FacilityName = reader.GetStringByName("facility_name"),
                FacilityType = Enum.Parse<FacilityType>(reader.GetStringByName("facility_type"), true),
                Role = Enum.Parse<UserRole>(reader.GetStringByName("role"), true),
                Status = Enum.Parse<UserStatus>(reader.GetStringByName("status"), true)
            };
        }
        return null;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetThreatStats()
    {
        try
        {
            // Running Event Alerts (status = Pending_Review or Pending_AI)
            var runningEventsResult = await _dbService.QueryScalarAsync(
                "SELECT COUNT(*) FROM threats WHERE status IN ('Pending_AI', 'Pending_Review')");
            var runningEvents = runningEventsResult != null ? Convert.ToInt32(runningEventsResult) : 0;
            
            // Running 24/7 Alerts (status = Approved, created in last 24 hours)
            var running247Result = await _dbService.QueryScalarAsync(
                "SELECT COUNT(*) FROM threats WHERE status = 'Approved' AND created_at >= DATE_SUB(NOW(), INTERVAL 24 HOUR)");
            var running247 = running247Result != null ? Convert.ToInt32(running247Result) : 0;
            
            // Total Alerts in Past 30 Days
            var past30DaysResult = await _dbService.QueryScalarAsync(
                "SELECT COUNT(*) FROM threats WHERE created_at >= DATE_SUB(NOW(), INTERVAL 30 DAY)");
            var past30Days = past30DaysResult != null ? Convert.ToInt32(past30DaysResult) : 0;
            
            // Total Alerts
            var totalResult = await _dbService.QueryScalarAsync("SELECT COUNT(*) FROM threats");
            var total = totalResult != null ? Convert.ToInt32(totalResult) : 0;
            
            return Ok(new
            {
                runningEvents,
                running247,
                past30Days,
                total
            });
        }
        catch (MyApp.Namespace.Services.DatabaseQuotaExceededException ex)
        {
            _logger.LogWarning(ex, "Database quota exceeded");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching threat stats");
            return StatusCode(500, new { message = "An error occurred while fetching statistics" });
        }
    }

    [HttpGet("incoming")]
    public async Task<IActionResult> GetIncomingThreats([FromQuery] string? status = null, [FromQuery] int limit = 10)
    {
        try
        {
            var query = @"SELECT t.id, t.title, t.description, t.category, t.source, t.date_observed, 
                                 t.impact_level, t.status, t.created_at, t.external_reference,
                                 u.full_name as submitter
                          FROM threats t
                          LEFT JOIN users u ON t.user_id = u.id
                          WHERE 1=1";
            
            var parameters = new List<object>();
            
            if (!string.IsNullOrEmpty(status))
            {
                query += " AND t.status = @p0";
                parameters.Add(status);
            }
            else
            {
                // Default to pending threats
                query += " AND t.status IN ('Pending_AI', 'Pending_Review')";
            }
            
            query += " ORDER BY t.created_at DESC LIMIT @p" + parameters.Count;
            parameters.Add(limit);

            var results = await _dbService.QueryAsync(query, parameters.ToArray());
            
            var threats = results.Select(row => new
            {
                id = Convert.ToInt32(row["id"]),
                title = row["title"]?.ToString() ?? "",
                description = row["description"]?.ToString() ?? "",
                category = row["category"]?.ToString(),
                source = row["source"]?.ToString() ?? "",
                dateObserved = row["date_observed"] != DBNull.Value ? DateTime.SpecifyKind(Convert.ToDateTime(row["date_observed"]), DateTimeKind.Utc) : (DateTime?)null,
                impactLevel = row["impact_level"]?.ToString(),
                status = row["status"]?.ToString() ?? "",
                createdAt = row["created_at"] != DBNull.Value ? DateTime.SpecifyKind(Convert.ToDateTime(row["created_at"]), DateTimeKind.Utc) : DateTime.UtcNow,
                externalReference = row["external_reference"]?.ToString(),
                submitter = row["submitter"]?.ToString() ?? "System"
            }).ToList();

            return Ok(new { threats });
        }
        catch (MyApp.Namespace.Services.DatabaseQuotaExceededException ex)
        {
            _logger.LogWarning(ex, "Database quota exceeded");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching incoming threats");
            return StatusCode(500, new { message = "An error occurred while fetching incoming threats" });
        }
    }

    [HttpGet("user/my-alerts")]
    public async Task<IActionResult> GetMyAlerts()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            var query = @"SELECT t.id, t.title, t.description, t.status, t.created_at
                          FROM threats t
                          WHERE t.user_id = @p0
                          ORDER BY t.created_at DESC";

            var results = await _dbService.QueryAsync(query, userId);

            var alerts = new List<object>();
            var statusCounts = new Dictionary<string, int>
            {
                { "new", 0 },
                { "suspended", 0 },
                { "due", 0 },
                { "inProgress", 0 },
                { "closed", 0 }
            };

            foreach (var row in results)
            {
                var status = row["status"]?.ToString() ?? "";
                var statusKey = status switch
                {
                    "Pending_AI" => "new",
                    "Pending_Review" => "inProgress",
                    "Approved" => "closed",
                    "Rejected" => "closed",
                    _ => "new"
                };
                statusCounts[statusKey]++;

                alerts.Add(new
                {
                    id = Convert.ToInt32(row["id"]),
                    title = row["title"]?.ToString() ?? "",
                    description = row["description"]?.ToString() ?? "",
                    status = status,
                    createdAt = row["created_at"] != DBNull.Value ? DateTime.SpecifyKind(Convert.ToDateTime(row["created_at"]), DateTimeKind.Utc) : DateTime.UtcNow
                });
            }

            return Ok(new { alerts, statusCounts });
        }
        catch (MyApp.Namespace.Services.DatabaseQuotaExceededException ex)
        {
            _logger.LogWarning(ex, "Database quota exceeded");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching my alerts");
            return StatusCode(500, new { message = "An error occurred while fetching alerts" });
        }
    }

    private async Task LogAuditAsync(int userId, int? threatId, string actionType, string details)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = "INSERT INTO audit_logs (threat_id, user_id, action_type, action_details, timestamp) VALUES (@threat_id, @user_id, @action_type, @action_details, NOW())";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@threat_id", threatId.HasValue ? (object)threatId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@user_id", userId);
            command.Parameters.AddWithValue("@action_type", actionType);
            command.Parameters.AddWithValue("@action_details", details);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit entry");
        }
    }
}


