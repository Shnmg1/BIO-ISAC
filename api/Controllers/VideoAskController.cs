using Microsoft.AspNetCore.Mvc;
using api.Models.VideoAsk;
using api.Services;

namespace api.Controllers;

/// <summary>
/// Controller for handling VideoAsk integration - webhook reception and response management.
/// 
/// SETUP INSTRUCTIONS FOR LOCAL TESTING:
/// ======================================
/// 1. Install ngrok: brew install ngrok (macOS) or download from https://ngrok.com
/// 2. Start your API: dotnet run (note the port, e.g., 5000 or 5001)
/// 3. In a new terminal, run: ngrok http 5000 (use your port)
/// 4. Copy the HTTPS forwarding URL (e.g., https://abc123.ngrok.io)
/// 5. In VideoAsk dashboard: Settings → Integrations → Webhooks
/// 6. Add webhook URL: https://abc123.ngrok.io/api/videoask/webhook
/// 7. Select events: "New Response" 
/// 8. Save and test by completing a VideoAsk form
/// 
/// The webhook endpoint will receive POST requests when respondents complete your VideoAsk.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class VideoAskController : ControllerBase
{
    private readonly VideoAskService _videoAskService;
    private readonly ILogger<VideoAskController> _logger;

    public VideoAskController(VideoAskService videoAskService, ILogger<VideoAskController> logger)
    {
        _videoAskService = videoAskService;
        _logger = logger;
    }

    /// <summary>
    /// Webhook endpoint to receive VideoAsk response notifications.
    /// Configure this URL in your VideoAsk dashboard under Settings → Integrations → Webhooks.
    /// </summary>
    /// <param name="payload">The webhook payload from VideoAsk containing response data.</param>
    /// <returns>200 OK if processed successfully, 400 if invalid payload.</returns>
    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook([FromBody] VideoAskWebhookPayload payload)
    {
        _logger.LogInformation("Received VideoAsk webhook for ContactId: {ContactId}", payload.ContactId);

        if (payload == null)
        {
            _logger.LogWarning("Received null VideoAsk webhook payload");
            return BadRequest(new { error = "Invalid payload" });
        }

        var success = await _videoAskService.ProcessWebhookAsync(payload);

        if (success)
        {
            return Ok(new
            {
                message = "Webhook received successfully",
                contactId = payload.ContactId,
                responseCount = _videoAskService.GetResponseCount()
            });
        }

        return BadRequest(new { error = "Failed to process webhook" });
    }

    /// <summary>
    /// Get all received screening responses.
    /// </summary>
    /// <returns>List of all VideoAsk responses received.</returns>
    [HttpGet("responses")]
    public async Task<IActionResult> GetAllResponses()
    {
        var responses = await _videoAskService.GetAllResponsesAsync();
        return Ok(new
        {
            count = _videoAskService.GetResponseCount(),
            responses = responses
        });
    }

    /// <summary>
    /// Get a specific screening response by contact ID.
    /// </summary>
    /// <param name="contactId">The VideoAsk contact ID.</param>
    /// <returns>The response details if found.</returns>
    [HttpGet("responses/{contactId}")]
    public async Task<IActionResult> GetResponseById(string contactId)
    {
        var response = await _videoAskService.GetResponseByIdAsync(contactId);

        if (response == null)
        {
            return NotFound(new { error = $"No response found for contact ID: {contactId}" });
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete a specific screening response by contact ID.
    /// </summary>
    /// <param name="contactId">The VideoAsk contact ID to delete.</param>
    /// <returns>Success message if deleted.</returns>
    [HttpDelete("responses/{contactId}")]
    public async Task<IActionResult> DeleteResponse(string contactId)
    {
        var deleted = await _videoAskService.DeleteResponseAsync(contactId);

        if (!deleted)
        {
            return NotFound(new { error = $"No response found for contact ID: {contactId}" });
        }

        return Ok(new { message = $"Response deleted for contact ID: {contactId}" });
    }

    /// <summary>
    /// Clear all stored responses (useful for testing).
    /// </summary>
    /// <returns>Success message.</returns>
    [HttpDelete("responses")]
    public IActionResult ClearAllResponses()
    {
        _videoAskService.ClearAllResponses();
        return Ok(new { message = "All responses cleared" });
    }

    /// <summary>
    /// Health check endpoint to verify the webhook is accessible.
    /// You can use this URL + /health in VideoAsk to test connectivity.
    /// </summary>
    /// <returns>Status indicating the webhook is ready.</returns>
    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            service = "VideoAsk Integration",
            responseCount = _videoAskService.GetResponseCount(),
            timestamp = DateTime.UtcNow
        });
    }
}
