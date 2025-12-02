using Microsoft.AspNetCore.Mvc;
using MyApp.Namespace.Services;
using api.Services;
using api.Models;

namespace api.Controllers
{
    [ApiController]
    [Route("api/threats")]
    public class ThreatRatingController : ControllerBase
    {
        private readonly DatabaseService _dbService;
        private readonly AIService _aiService;
        private readonly ILogger<ThreatRatingController> _logger;

        public ThreatRatingController(
            DatabaseService dbService,
            AIService aiService,
            ILogger<ThreatRatingController> logger)
        {
            _dbService = dbService;
            _aiService = aiService;
            _logger = logger;
        }

        /// <summary>
        /// Get AI rating/classification for a specific threat
        /// </summary>
        [HttpGet("{id}/ai-rating")]
        public async Task<IActionResult> GetAIRating(int id)
        {
            try
            {
                using var connection = await _dbService.GetConnectionAsync();
                var query = @"
                    SELECT c.* FROM classifications c
                    WHERE c.threat_id = @threatId
                    ORDER BY c.created_at DESC
                    LIMIT 1";

                using var command = new MySqlConnector.MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@threatId", id);

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return NotFound(new { message = $"No AI rating found for threat {id}" });
                }

                var rating = new
                {
                    threat_id = reader.GetInt32("threat_id"),
                    ai_tier = reader.GetString("ai_tier"),
                    ai_confidence = reader.GetDecimal("ai_confidence"),
                    ai_reasoning = reader.GetString("ai_reasoning"),
                    ai_actions = reader.GetString("ai_actions"),
                    created_at = reader.GetDateTime("created_at")
                };

                return Ok(rating);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving AI rating for threat {id}");
                return StatusCode(500, new { message = "Error retrieving AI rating", error = ex.Message });
            }
        }

        /// <summary>
        /// Manually trigger AI rating for a specific threat
        /// </summary>
        [HttpPost("{id}/rate")]
        public async Task<IActionResult> RateThreat(int id)
        {
            try
            {
                // First, fetch the threat
                using var connection = await _dbService.GetConnectionAsync();
                var query = "SELECT * FROM threats WHERE id = @id";

                using var command = new MySqlConnector.MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                Threat? threat = null;
                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    threat = new Threat
                    {
                        id = reader.GetInt32("id"),
                        title = reader.GetString("title"),
                        description = reader.GetString("description"),
                        category = reader.GetString("category"),
                        source = reader.GetString("source"),
                        date_observed = reader.GetDateTime("date_observed"),
                        impact_level = reader.GetString("impact_level"),
                        external_reference = reader.IsDBNull(reader.GetOrdinal("external_reference"))
                            ? null
                            : reader.GetString("external_reference")
                    };
                }

                if (threat == null)
                {
                    return NotFound(new { message = $"Threat {id} not found" });
                }

                // Classify the threat
                _logger.LogInformation($"Manually rating threat {id}: {threat.title}");
                var classification = await _aiService.ClassifyThreatAsync(threat);

                // Check if classification already exists
                var existingQuery = "SELECT id FROM classifications WHERE threat_id = @threatId";
                using var existingCommand = new MySqlConnector.MySqlCommand(existingQuery, connection);
                existingCommand.Parameters.AddWithValue("@threatId", id);

                var existingId = await existingCommand.ExecuteScalarAsync();

                if (existingId != null)
                {
                    // Update existing classification
                    var updateQuery = @"
                        UPDATE classifications
                        SET ai_tier = @ai_tier,
                            ai_confidence = @ai_confidence,
                            ai_reasoning = @ai_reasoning,
                            ai_actions = @ai_actions,
                            created_at = NOW()
                        WHERE threat_id = @threat_id";

                    using var updateCommand = new MySqlConnector.MySqlCommand(updateQuery, connection);
                    updateCommand.Parameters.AddWithValue("@threat_id", id);
                    updateCommand.Parameters.AddWithValue("@ai_tier", classification.Tier.ToString());
                    updateCommand.Parameters.AddWithValue("@ai_confidence", classification.Confidence);
                    updateCommand.Parameters.AddWithValue("@ai_reasoning", classification.Reasoning);
                    updateCommand.Parameters.AddWithValue("@ai_actions", classification.RecommendedActions);

                    await updateCommand.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Updated AI rating for threat {id}");
                }
                else
                {
                    // Create new classification
                    await _aiService.SaveClassificationAsync(id, classification);
                    _logger.LogInformation($"Created AI rating for threat {id}");
                }

                var result = new
                {
                    threat_id = id,
                    ai_tier = classification.Tier.ToString(),
                    ai_confidence = classification.Confidence,
                    ai_reasoning = classification.Reasoning,
                    ai_actions = classification.RecommendedActions,
                    created_at = DateTime.Now
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rating threat {id}");
                return StatusCode(500, new { message = "Error rating threat", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all threats that don't have AI ratings yet
        /// </summary>
        [HttpGet("unrated")]
        public async Task<IActionResult> GetUnratedThreats(
            [FromQuery] int limit = 50,
            [FromQuery] string? source = null,
            [FromQuery] string? category = null)
        {
            try
            {
                using var connection = await _dbService.GetConnectionAsync();

                var queryBuilder = new System.Text.StringBuilder(@"
                    SELECT t.* FROM threats t
                    LEFT JOIN classifications c ON t.id = c.threat_id
                    WHERE c.id IS NULL");

                if (!string.IsNullOrEmpty(source))
                {
                    queryBuilder.Append(" AND t.source = @source");
                }

                if (!string.IsNullOrEmpty(category))
                {
                    queryBuilder.Append(" AND t.category = @category");
                }

                queryBuilder.Append(" ORDER BY t.date_observed DESC LIMIT @limit");

                using var command = new MySqlConnector.MySqlCommand(queryBuilder.ToString(), connection);
                command.Parameters.AddWithValue("@limit", limit);

                if (!string.IsNullOrEmpty(source))
                {
                    command.Parameters.AddWithValue("@source", source);
                }

                if (!string.IsNullOrEmpty(category))
                {
                    command.Parameters.AddWithValue("@category", category);
                }

                var threats = new List<object>();
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    threats.Add(new
                    {
                        id = reader.GetInt32("id"),
                        title = reader.GetString("title"),
                        description = reader.GetString("description"),
                        category = reader.GetString("category"),
                        source = reader.GetString("source"),
                        date_observed = reader.GetDateTime("date_observed"),
                        impact_level = reader.GetString("impact_level"),
                        external_reference = reader.IsDBNull(reader.GetOrdinal("external_reference"))
                            ? null
                            : reader.GetString("external_reference"),
                        status = reader.GetString("status")
                    });
                }

                return Ok(new
                {
                    count = threats.Count,
                    threats = threats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unrated threats");
                return StatusCode(500, new { message = "Error retrieving unrated threats", error = ex.Message });
            }
        }
    }
}
