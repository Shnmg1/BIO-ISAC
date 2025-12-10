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
public class ClassificationController : ControllerBase
{
    private readonly AIService _aiService;
    private readonly DatabaseService _dbService;
    private readonly ILogger<ClassificationController> _logger;

    public ClassificationController(AIService aiService, DatabaseService dbService, ILogger<ClassificationController> logger)
    {
        _aiService = aiService;
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

    [HttpPost("analyze/{threatId}")]
    public async Task<IActionResult> AnalyzeThreat(int threatId)
    {
        try
        {
            // Get threat from database
            var threat = await GetThreatAsync(threatId);
            if (threat == null)
            {
                return NotFound(new { message = "Threat not found" });
            }

            // Update threat status to Pending_AI if not already
            await UpdateThreatStatusAsync(threatId, "Pending_AI");

            // Classify with AI
            var classification = await _aiService.ClassifyThreatAsync(threat);

            // Save classification
            await _aiService.SaveClassificationAsync(threatId, classification);

            // Update threat status to Pending_Review
            await UpdateThreatStatusAsync(threatId, "Pending_Review");

            // Log audit
            var userId = GetCurrentUserId();
            await LogAuditAsync(userId, threatId, "AI_Classification", $"AI classified threat as {classification.Tier} with {classification.Confidence}% confidence");

            return Ok(new
            {
                threatId,
                classification = new
                {
                    tier = classification.Tier.ToString(),
                    confidence = classification.Confidence,
                    reasoning = classification.Reasoning,
                    recommendedActions = classification.RecommendedActions,
                    nextSteps = classification.NextSteps,
                    keywords = classification.Keywords,
                    bioSectorRelevance = classification.BioSectorRelevance,
                    recommendedIndustry = classification.RecommendedIndustry,
                    specificIndustry = classification.SpecificIndustry
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing threat");
            return StatusCode(500, new { message = "An error occurred during AI analysis" });
        }
    }

    private async Task<Threat?> GetThreatAsync(int threatId)
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

    private async Task UpdateThreatStatusAsync(int threatId, string status)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "UPDATE threats SET status = @status WHERE id = @id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@id", threatId);
        await command.ExecuteNonQueryAsync();
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

