using System.Text.Json.Serialization;

namespace api.Models.VideoAsk;

/// <summary>
/// Represents an individual question response within a VideoAsk submission.
/// </summary>
public class VideoAskQuestionResponse
{
    /// <summary>
    /// The question text that was asked.
    /// </summary>
    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>
    /// The type of answer: "video", "audio", "text", "multiple_choice", etc.
    /// </summary>
    [JsonPropertyName("answer_type")]
    public string AnswerType { get; set; } = string.Empty;

    /// <summary>
    /// The answer value (for text or multiple choice responses).
    /// </summary>
    [JsonPropertyName("answer_value")]
    public string? AnswerValue { get; set; }

    /// <summary>
    /// URL to the video response (if answer_type is "video").
    /// </summary>
    [JsonPropertyName("video_url")]
    public string? VideoUrl { get; set; }

    /// <summary>
    /// URL to the audio response (if answer_type is "audio").
    /// </summary>
    [JsonPropertyName("audio_url")]
    public string? AudioUrl { get; set; }

    /// <summary>
    /// Transcription of the video/audio response (if available).
    /// </summary>
    [JsonPropertyName("transcription")]
    public string? Transcription { get; set; }

    /// <summary>
    /// Duration of the video/audio response in seconds (if applicable).
    /// </summary>
    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; set; }
}
