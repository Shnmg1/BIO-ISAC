using System.Collections.Concurrent;
using api.Models.VideoAsk;

namespace api.Services;

/// <summary>
/// Service for handling VideoAsk webhook data and storing screening responses in-memory.
/// For production use, replace in-memory storage with database persistence.
/// </summary>
public class VideoAskService
{
    private readonly ILogger<VideoAskService> _logger;
    private readonly ConcurrentDictionary<string, VideoAskWebhookPayload> _responses = new();

    public VideoAskService(ILogger<VideoAskService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes and stores an incoming VideoAsk webhook payload.
    /// </summary>
    /// <param name="payload">The webhook payload from VideoAsk.</param>
    /// <returns>True if successfully processed, false otherwise.</returns>
    public Task<bool> ProcessWebhookAsync(VideoAskWebhookPayload payload)
    {
        try
        {
            if (string.IsNullOrEmpty(payload.ContactId))
            {
                _logger.LogWarning("Received VideoAsk webhook with empty ContactId");
                return Task.FromResult(false);
            }

            payload.ReceivedAt = DateTime.UtcNow;

            _responses.AddOrUpdate(
                payload.ContactId,
                payload,
                (key, existing) => payload // Update if exists
            );

            _logger.LogInformation(
                "Processed VideoAsk response from {ContactName} ({ContactEmail}) for form {FormTitle}. Total responses stored: {Count}",
                payload.ContactName ?? "Unknown",
                payload.ContactEmail ?? "No email",
                payload.FormTitle ?? payload.FormId,
                _responses.Count
            );

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing VideoAsk webhook for ContactId: {ContactId}", payload.ContactId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Retrieves all stored screening responses.
    /// </summary>
    /// <returns>List of all VideoAsk responses received.</returns>
    public Task<IEnumerable<VideoAskWebhookPayload>> GetAllResponsesAsync()
    {
        var responses = _responses.Values
            .OrderByDescending(r => r.ReceivedAt)
            .ToList();

        return Task.FromResult<IEnumerable<VideoAskWebhookPayload>>(responses);
    }

    /// <summary>
    /// Retrieves a specific screening response by contact ID.
    /// </summary>
    /// <param name="contactId">The VideoAsk contact ID.</param>
    /// <returns>The response if found, null otherwise.</returns>
    public Task<VideoAskWebhookPayload?> GetResponseByIdAsync(string contactId)
    {
        _responses.TryGetValue(contactId, out var response);
        return Task.FromResult(response);
    }

    /// <summary>
    /// Gets the count of stored responses.
    /// </summary>
    /// <returns>Number of responses in storage.</returns>
    public int GetResponseCount() => _responses.Count;

    /// <summary>
    /// Clears all stored responses (useful for testing).
    /// </summary>
    public void ClearAllResponses()
    {
        _responses.Clear();
        _logger.LogInformation("Cleared all VideoAsk responses from in-memory storage");
    }

    /// <summary>
    /// Deletes a specific response by contact ID.
    /// </summary>
    /// <param name="contactId">The VideoAsk contact ID to delete.</param>
    /// <returns>True if deleted, false if not found.</returns>
    public Task<bool> DeleteResponseAsync(string contactId)
    {
        var removed = _responses.TryRemove(contactId, out _);
        if (removed)
        {
            _logger.LogInformation("Deleted VideoAsk response for ContactId: {ContactId}", contactId);
        }
        return Task.FromResult(removed);
    }
}
