using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Models;
using api.Services;
using MyApp.Namespace.Services;
using api.Extensions;
using MySqlConnector;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessageController : ControllerBase
{
    private readonly DatabaseService _dbService;
    private readonly ILogger<MessageController> _logger;

    public MessageController(DatabaseService dbService, ILogger<MessageController> logger)
    {
        _dbService = dbService;
        _logger = logger;
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
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        try
        {
            var fromUserId = GetCurrentUserId();
            if (fromUserId == 0) return Unauthorized();

            // If toUserId is not specified, send to admins
            int toUserId;
            if (request.ToUserId.HasValue)
            {
                toUserId = request.ToUserId.Value;
            }
            else
            {
                // Get first available admin
                var admin = await GetFirstAdminAsync();
                if (admin == null)
                {
                    return BadRequest(new { message = "No admin available" });
                }
                toUserId = admin.Id;
            }

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"INSERT INTO messages (from_user_id, to_user_id, subject, body, threat_id, created_at) 
                         VALUES (@from_user_id, @to_user_id, @subject, @body, @threat_id, NOW())";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@from_user_id", fromUserId);
            command.Parameters.AddWithValue("@to_user_id", toUserId);
            command.Parameters.AddWithValue("@subject", request.Subject);
            command.Parameters.AddWithValue("@body", request.Body);
            command.Parameters.AddWithValue("@threat_id", request.ThreatId.HasValue ? (object)request.ThreatId.Value : DBNull.Value);

            await command.ExecuteNonQueryAsync();
            var messageId = (int)command.LastInsertedId;

            await LogAuditAsync(fromUserId, null, "Message_Sent", $"Message sent to user {toUserId}");

            return Ok(new { id = messageId, message = "Message sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            return StatusCode(500, new { message = "An error occurred while sending the message" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages([FromQuery] bool? unreadOnly = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Unauthorized();

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT m.id, m.from_user_id, m.to_user_id, m.subject, m.body, m.threat_id, m.read_at, m.created_at,
                                 u1.full_name as from_user_name, u2.full_name as to_user_name
                          FROM messages m
                          LEFT JOIN users u1 ON m.from_user_id = u1.id
                          LEFT JOIN users u2 ON m.to_user_id = u2.id
                          WHERE m.from_user_id = @user_id OR m.to_user_id = @user_id";

            if (unreadOnly == true)
            {
                query += " AND m.read_at IS NULL AND m.to_user_id = @user_id";
            }

            query += " ORDER BY m.created_at DESC";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@user_id", userId);

            var messages = new List<object>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(new
                {
                    id = reader.GetInt32ByName("id"),
                    fromUserId = reader.GetInt32ByName("from_user_id"),
                    fromUserName = reader.IsDBNullByName("from_user_name") ? null : reader.GetStringByName("from_user_name"),
                    toUserId = reader.GetInt32ByName("to_user_id"),
                    toUserName = reader.IsDBNullByName("to_user_name") ? null : reader.GetStringByName("to_user_name"),
                    subject = reader.GetStringByName("subject"),
                    body = reader.GetStringByName("body"),
                    threatId = reader.IsDBNullByName("threat_id") ? (int?)null : reader.GetInt32ByName("threat_id"),
                    readAt = reader.IsDBNullByName("read_at") ? (DateTime?)null : reader.GetDateTimeByName("read_at"),
                    createdAt = reader.GetDateTimeByName("created_at"),
                    isRead = !reader.IsDBNullByName("read_at")
                });
            }

            return Ok(messages);
        }
        catch (MyApp.Namespace.Services.DatabaseQuotaExceededException ex)
        {
            _logger.LogWarning(ex, "Database quota exceeded");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching messages");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMessage(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Unauthorized();

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT m.id, m.from_user_id, m.to_user_id, m.subject, m.body, m.threat_id, m.read_at, m.created_at,
                                 u1.full_name as from_user_name, u1.email as from_user_email,
                                 u2.full_name as to_user_name, u2.email as to_user_email
                          FROM messages m
                          LEFT JOIN users u1 ON m.from_user_id = u1.id
                          LEFT JOIN users u2 ON m.to_user_id = u2.id
                          WHERE m.id = @id AND (m.from_user_id = @user_id OR m.to_user_id = @user_id)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@user_id", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                // Mark as read if user is recipient
                if (reader.GetInt32ByName("to_user_id") == userId && reader.IsDBNullByName("read_at"))
                {
                    await MarkAsReadAsync(id);
                }

                return Ok(new
                {
                    id = reader.GetInt32ByName("id"),
                    fromUser = new
                    {
                        id = reader.GetInt32ByName("from_user_id"),
                        name = reader.IsDBNullByName("from_user_name") ? null : reader.GetStringByName("from_user_name"),
                        email = reader.IsDBNullByName("from_user_email") ? null : reader.GetStringByName("from_user_email")
                    },
                    toUser = new
                    {
                        id = reader.GetInt32ByName("to_user_id"),
                        name = reader.IsDBNullByName("to_user_name") ? null : reader.GetStringByName("to_user_name"),
                        email = reader.IsDBNullByName("to_user_email") ? null : reader.GetStringByName("to_user_email")
                    },
                    subject = reader.GetStringByName("subject"),
                    body = reader.GetStringByName("body"),
                    threatId = reader.IsDBNullByName("threat_id") ? (int?)null : reader.GetInt32ByName("threat_id"),
                    readAt = reader.IsDBNullByName("read_at") ? (DateTime?)null : reader.GetDateTimeByName("read_at"),
                    createdAt = reader.GetDateTimeByName("created_at")
                });
            }

            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching message");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Unauthorized();

            await MarkAsReadAsync(id);
            return Ok(new { message = "Message marked as read" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message as read");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    private async Task MarkAsReadAsync(int messageId)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "UPDATE messages SET read_at = NOW() WHERE id = @id AND read_at IS NULL";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", messageId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<User?> GetFirstAdminAsync()
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, full_name FROM users WHERE role = 'Admin' AND status = 'Active' LIMIT 1";
        using var command = new MySqlCommand(query, connection);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name")
            };
        }
        return null;
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

[ApiController]
[Route("api/admin/[controller]")]
public class AdminMessageController : ControllerBase
{
    private readonly DatabaseService _dbService;
    private readonly ILogger<AdminMessageController> _logger;

    public AdminMessageController(DatabaseService dbService, ILogger<AdminMessageController> logger)
    {
        _dbService = dbService;
        _logger = logger;
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

    [HttpGet]
    public async Task<IActionResult> GetAdminInbox([FromQuery] bool? unreadOnly = null, [FromQuery] string? facilityType = null)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT m.id, m.from_user_id, m.to_user_id, m.subject, m.body, m.threat_id, m.read_at, m.created_at,
                                 u1.full_name as from_user_name, u1.facility_type, u1.facility_name,
                                 u2.full_name as to_user_name
                          FROM messages m
                          LEFT JOIN users u1 ON m.from_user_id = u1.id
                          LEFT JOIN users u2 ON m.to_user_id = u2.id
                          WHERE m.to_user_id IN (SELECT id FROM users WHERE role = 'Admin')";

            if (unreadOnly == true)
            {
                query += " AND m.read_at IS NULL";
            }

            if (!string.IsNullOrEmpty(facilityType))
            {
                query += " AND u1.facility_type = @facility_type";
            }

            query += " ORDER BY m.created_at DESC";

            using var command = new MySqlConnector.MySqlCommand(query, connection);
            if (!string.IsNullOrEmpty(facilityType))
            {
                command.Parameters.AddWithValue("@facility_type", facilityType);
            }

            var messages = new List<object>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(new
                {
                    id = reader.GetInt32ByName("id"),
                    fromUserId = reader.GetInt32ByName("from_user_id"),
                    fromUserName = reader.IsDBNullByName("from_user_name") ? null : reader.GetStringByName("from_user_name"),
                    toUserId = reader.GetInt32ByName("to_user_id"),
                    toUserName = reader.IsDBNullByName("to_user_name") ? null : reader.GetStringByName("to_user_name"),
                    facilityType = reader.IsDBNullByName("facility_type") ? null : reader.GetStringByName("facility_type"),
                    facilityName = reader.IsDBNullByName("facility_name") ? null : reader.GetStringByName("facility_name"),
                    subject = reader.GetStringByName("subject"),
                    body = reader.GetStringByName("body"),
                    threatId = reader.IsDBNullByName("threat_id") ? (int?)null : reader.GetInt32ByName("threat_id"),
                    readAt = reader.IsDBNullByName("read_at") ? (DateTime?)null : reader.GetDateTimeByName("read_at"),
                    createdAt = reader.GetDateTimeByName("created_at"),
                    isRead = !reader.IsDBNullByName("read_at")
                });
            }

            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching admin inbox");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [HttpPost("{id}/reply")]
    public async Task<IActionResult> ReplyToMessage(int id, [FromBody] ReplyMessageRequest request)
    {
        try
        {
            var adminId = GetCurrentUserId();
            if (adminId == 0) return Unauthorized();

            // Get original message
            using var connection = await _dbService.GetConnectionAsync();
            var getMessageQuery = "SELECT from_user_id, threat_id FROM messages WHERE id = @id";
            using var getMessageCommand = new MySqlConnector.MySqlCommand(getMessageQuery, connection);
            getMessageCommand.Parameters.AddWithValue("@id", id);
            
            int toUserId = 0;
            int? threatId = null;
            using var reader = await getMessageCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                toUserId = reader.GetInt32ByName("from_user_id");
                threatId = reader.IsDBNullByName("threat_id") ? (int?)null : reader.GetInt32ByName("threat_id");
            }
            else
            {
                return NotFound();
            }

            // Create reply
            var replyQuery = @"INSERT INTO messages (from_user_id, to_user_id, subject, body, threat_id, created_at) 
                              VALUES (@from_user_id, @to_user_id, @subject, @body, @threat_id, NOW())";
            using var replyCommand = new MySqlConnector.MySqlCommand(replyQuery, connection);
            replyCommand.Parameters.AddWithValue("@from_user_id", adminId);
            replyCommand.Parameters.AddWithValue("@to_user_id", toUserId);
            replyCommand.Parameters.AddWithValue("@subject", $"Re: {request.Subject}");
            replyCommand.Parameters.AddWithValue("@body", request.Body);
            replyCommand.Parameters.AddWithValue("@threat_id", threatId.HasValue ? (object)threatId.Value : DBNull.Value);

            await replyCommand.ExecuteNonQueryAsync();

            return Ok(new { message = "Reply sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replying to message");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }
}

// DTOs
public class SendMessageRequest
{
    public int? ToUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? ThreatId { get; set; }
}

public class ReplyMessageRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

