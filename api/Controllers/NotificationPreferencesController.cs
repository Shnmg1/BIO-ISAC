using Microsoft.AspNetCore.Mvc;
using api.DTOs;
using api.Services;
using MyApp.Namespace.Services;
using api.Extensions;
using MySqlConnector;
using System.Text.Json;

namespace api.Controllers;

[ApiController]
[Route("api/user/notification-preferences")]
public class NotificationPreferencesController : ControllerBase
{
    private readonly DatabaseService _dbService;
    private readonly ILogger<NotificationPreferencesController> _logger;

    public NotificationPreferencesController(DatabaseService dbService, ILogger<NotificationPreferencesController> logger)
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

    [HttpGet]
    public async Task<IActionResult> GetPreferences()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            using var connection = await _dbService.GetConnectionAsync();
            
            // Check if column exists first
            try
            {
            var query = "SELECT notification_preferences FROM users WHERE id = @id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var preferencesJson = reader.IsDBNullByName("notification_preferences") 
                    ? null 
                    : reader.GetStringByName("notification_preferences");

                if (string.IsNullOrEmpty(preferencesJson))
                {
                    // Return default preferences
                    return Ok(new NotificationPreferencesRequest
                    {
                        DeliveryMode = NotificationDeliveryMode.Immediate,
                        ReceiveHighTier = true,
                        ReceiveMediumTier = true,
                        ReceiveLowTier = false
                    });
                }

                var preferences = JsonSerializer.Deserialize<NotificationPreferencesRequest>(preferencesJson);
                return Ok(preferences ?? new NotificationPreferencesRequest());
            }

            return NotFound(new { message = "User not found" });
            }
            catch (MySqlException dbEx) when (dbEx.Number == 1054) // Unknown column
            {
                _logger.LogWarning("notification_preferences column does not exist, attempting to add it");
                try
                {
                    await AddNotificationPreferencesColumnAsync();
                    // Return default preferences after adding column
                    return Ok(new NotificationPreferencesRequest
                    {
                        DeliveryMode = NotificationDeliveryMode.Immediate,
                        ReceiveHighTier = true,
                        ReceiveMediumTier = true,
                        ReceiveLowTier = false
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding notification_preferences column");
                    return StatusCode(500, new { message = "Database schema update required. Please run migration to add notification_preferences column to users table." });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notification preferences");
            return StatusCode(500, new { message = "An error occurred while fetching preferences" });
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdatePreferences([FromBody] NotificationPreferencesRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            // Validation
            if (request.DeliveryMode == NotificationDeliveryMode.DailyDigest && string.IsNullOrEmpty(request.DailyDigestTime))
            {
                return BadRequest(new { message = "Daily digest time is required when delivery mode is DailyDigest" });
            }

            if (request.DeliveryMode == NotificationDeliveryMode.WeeklyDigest && string.IsNullOrEmpty(request.WeeklyDigestDay))
            {
                return BadRequest(new { message = "Weekly digest day is required when delivery mode is WeeklyDigest" });
            }

            // Validate time format (HH:mm)
            if (!string.IsNullOrEmpty(request.DailyDigestTime) && !System.Text.RegularExpressions.Regex.IsMatch(request.DailyDigestTime, @"^([0-1][0-9]|2[0-3]):[0-5][0-9]$"))
            {
                return BadRequest(new { message = "Daily digest time must be in HH:mm format (e.g., 09:00)" });
            }

            // Validate day of week
            var validDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            if (!string.IsNullOrEmpty(request.WeeklyDigestDay) && !validDays.Contains(request.WeeklyDigestDay))
            {
                return BadRequest(new { message = "Weekly digest day must be a valid day of the week" });
            }

            var preferencesJson = JsonSerializer.Serialize(request);

            using var connection = await _dbService.GetConnectionAsync();
            
            // Check if notification_preferences column exists, if not we'll need to add it
            // For now, assume it exists or will be added via migration
            var query = "UPDATE users SET notification_preferences = @preferences WHERE id = @id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@preferences", preferencesJson);
            command.Parameters.AddWithValue("@id", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected == 0)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(new { message = "Notification preferences updated successfully" });
        }
        catch (MySqlException dbEx)
        {
            // If column doesn't exist, we need to add it
            if (dbEx.Number == 1054) // Unknown column
            {
                _logger.LogWarning("notification_preferences column does not exist, attempting to add it");
                try
                {
                    await AddNotificationPreferencesColumnAsync();
                    // Retry the update
                    return await UpdatePreferences(request);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding notification_preferences column");
                    return StatusCode(500, new { message = "Database schema update required. Please run migration to add notification_preferences column to users table." });
                }
            }
            _logger.LogError(dbEx, "Database error updating preferences");
            return StatusCode(500, new { message = "Database error occurred" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating notification preferences");
            return StatusCode(500, new { message = "An error occurred while updating preferences" });
        }
    }

    private async Task AddNotificationPreferencesColumnAsync()
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "ALTER TABLE users ADD COLUMN notification_preferences TEXT NULL";
        using var command = new MySqlCommand(query, connection);
        await command.ExecuteNonQueryAsync();
    }
}

