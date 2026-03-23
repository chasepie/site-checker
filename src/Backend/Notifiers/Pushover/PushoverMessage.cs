using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiteChecker.Backend.Notifiers.Pushover;

/// <summary>
/// https://pushover.net/api
/// </summary>
public class PushoverContents
{
    /// <summary>
    /// The message content (required for Pushover API)
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>
    /// A binary image attachment to send with the message
    /// </summary>
    [JsonPropertyName("attachment")]
    public byte[]? Attachment { get; set; }

    /// <summary>
    /// A Base64-encoded image attachment to send with the message
    /// </summary>
    [JsonPropertyName("attachment_base64")]
    public string? AttachmentBase64 { get; set; }

    /// <summary>
    /// The MIME type of the included attachment or attachment_base64
    /// </summary>
    [JsonPropertyName("attachment_type")]
    public string? AttachmentType { get; set; }

    /// <summary>
    /// The name of one of your devices to send just to that device instead of all devices
    /// </summary>
    [JsonPropertyName("device")]
    public string? Device { get; set; }

    /// <summary>
    /// Set to true to enable HTML parsing
    /// </summary>
    [JsonPropertyName("html")]
    [JsonConverter(typeof(NullableBoolToIntConverter))]
    public bool? Html { get; set; }

    /// <summary>
    /// A value of -2, -1, 0 (default), 1, or 2
    /// </summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    /// <summary>
    /// The name of a supported sound to override your default sound choice
    /// </summary>
    [JsonPropertyName("sound")]
    public string? Sound { get; set; }

    /// <summary>
    /// A Unix timestamp of a time to display instead of when our API received it
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    /// <summary>
    /// Your message's title, otherwise your app's name is used
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// A number of seconds that the message will live, before being deleted automatically
    /// </summary>
    [JsonPropertyName("ttl")]
    public int? TimeToLive { get; set; }

    /// <summary>
    /// A supplementary URL to show with your message
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// A title for the URL specified as the url parameter, otherwise just the URL is shown
    /// </summary>
    [JsonPropertyName("url_title")]
    public string? UrlTitle { get; set; }
}

/// <summary>
/// Custom JsonConverter that converts nullable boolean values to integers (true = 1, false/null = 0)
/// </summary>
internal sealed class NullableBoolToIntConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return reader.GetInt32() == 1;
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((value ?? false) ? 1 : 0);
    }
}
