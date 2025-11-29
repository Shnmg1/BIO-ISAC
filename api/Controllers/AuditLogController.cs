using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Services;
using MyApp.Namespace.Services;
using api.Extensions;
using api.DTOs;

namespace api.Controllers;

[ApiController]
[Route("api/admin/audit-logs")]
public class AuditLogController : ControllerBase
{
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<AuditLogController> _logger;

    public AuditLogController(AuditLogService auditLogService, ILogger<AuditLogController> logger)
    {
        _auditLogService = auditLogService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int? threatId, [FromQuery] int? userId, 
        [FromQuery] string? actionType, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            var filter = new AuditLogFilter
            {
                ThreatId = threatId,
                UserId = userId,
                ActionType = actionType,
                StartDate = startDate,
                EndDate = endDate,
                Limit = pageSize,
                Offset = (page - 1) * pageSize
            };

            var logs = await _auditLogService.GetAuditLogsAsync(filter);
            var totalCount = await _auditLogService.GetAuditLogCountAsync(filter);

            return Ok(new
            {
                logs,
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching audit logs");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportAuditLogs([FromQuery] int? threatId, [FromQuery] int? userId,
        [FromQuery] string? actionType, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        try
        {
            var filter = new AuditLogFilter
            {
                ThreatId = threatId,
                UserId = userId,
                ActionType = actionType,
                StartDate = startDate,
                EndDate = endDate,
                Limit = 10000 // Large limit for export
            };

            var logs = await _auditLogService.GetAuditLogsAsync(filter);

            // Generate CSV
            var csv = "Timestamp,User,Action Type,Threat ID,Details\n";
            foreach (var log in logs)
            {
                var userDisplay = log.UserId == 1
                    ? "System"
                    : (string.IsNullOrWhiteSpace(log.UserName) ? $"User {log.UserId}" : log.UserName);

                csv += $"{log.Timestamp:yyyy-MM-dd HH:mm:ss},{userDisplay},{log.ActionType},{log.ThreatId?.ToString() ?? ""},\"{log.Details?.Replace("\"", "\"\"") ?? ""}\"\n";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", $"audit_logs_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting audit logs");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }
}

