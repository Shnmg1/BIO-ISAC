using System.Text.Json.Serialization;

namespace api.Models.VideoAsk;

/// <summary>
/// Represents the webhook payload received from VideoAsk when a respondent completes a screening.
/// </summary>
public class VideoAskWebhookPayload
{
    /// <summary>
    /// Unique identifier for this response/contact in VideoAsk.
    /// </summary>
    [JsonPropertyName("contact_id")]
    public string ContactId { get; set; } = string.Empty;

    /// <summary>
    /// Email address of the respondent (if collected).
    /// </summary>
    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Name of the respondent (if collected).
    /// </summary>
    [JsonPropertyName("contact_name")]
    public string? ContactName { get; set; }

    /// <summary>
    /// Phone number of the respondent (if collected).
    /// </summary>
    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; set; }

    /// <summary>
    /// The VideoAsk form/videoask ID this response belongs to.
    /// </summary>
    [JsonPropertyName("form_id")]
    public string FormId { get; set; } = string.Empty;

    /// <summary>
    /// Title/name of the VideoAsk form.
    /// </summary>
    [JsonPropertyName("form_title")]
    public string? FormTitle { get; set; }

    /// <summary>
    /// When the response was created/submitted.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The individual question responses from the respondent.
    /// </summary>
    [JsonPropertyName("responses")]
    public List<VideoAskQuestionResponse> Responses { get; set; } = new();

    /// <summary>
    /// Internal timestamp for when this payload was received by our system.
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
