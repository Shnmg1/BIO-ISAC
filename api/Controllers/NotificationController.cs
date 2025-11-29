using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Models;
using api.Services;
using MyApp.Namespace.Services;
using api.Extensions;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly NotificationService _notificationService;
    private readonly DatabaseService _dbService;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(NotificationService notificationService, DatabaseService dbService, ILogger<NotificationController> logger)
    {
        _notificationService = notificationService;
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
    public async Task<IActionResult> GetUserNotifications()
    {
        try
        {
            var userId = GetCurrentUserId();

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT id, threat_id, tier, subject, body, delivery_status, sent_at, created_at 
                         FROM notifications 
                         WHERE sent_to = @user_id OR sent_to_all = TRUE
                         ORDER BY created_at DESC 
                         LIMIT 50";

            using var command = new MySqlConnector.MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@user_id", userId);

            var notifications = new List<object>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notifications.Add(new
                {
                    id = reader.GetInt32ByName("id"),
                    threatId = reader.IsDBNullByName("threat_id") ? (int?)null : reader.GetInt32ByName("threat_id"),
                    tier = reader.GetStringByName("tier"),
                    subject = reader.GetStringByName("subject"),
                    body = reader.GetStringByName("body"),
                    deliveryStatus = reader.GetStringByName("delivery_status"),
                    sentAt = reader.IsDBNullByName("sent_at") ? (DateTime?)null : reader.GetDateTimeByName("sent_at"),
                    createdAt = reader.GetDateTimeByName("created_at")
                });
            }

            return Ok(notifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notifications");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }
}

[ApiController]
[Route("api/admin/[controller]")]
public class AdminNotificationController : ControllerBase
{
    private readonly NotificationService _notificationService;
    private readonly ILogger<AdminNotificationController> _logger;

    public AdminNotificationController(NotificationService notificationService, ILogger<AdminNotificationController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendMassAlert([FromBody] MassAlertRequest request)
    {
        try
        {
            await _notificationService.SendMassAlertAsync(request);
            return Ok(new { message = "Mass alert sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending mass alert");
            return StatusCode(500, new { message = "An error occurred while sending the alert" });
        }
    }
}

