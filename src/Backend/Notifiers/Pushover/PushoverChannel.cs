using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Notifiers.Pushover;

/// <summary>
/// Sends notifications through Pushover, at the priority the Site configures for the outcome.
/// Pushover is the only channel that attaches the screenshot.
/// </summary>
public sealed class PushoverChannel(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PushoverChannel> logger)
    : INotificationChannel
{
    public const string PushoverUserKey = "PUSHOVER_USER";
    public const string PushoverTokenKey = "PUSHOVER_TOKEN";
    public const int MaxAttachmentSize = 5 * 1024 * 1024; // 5 MB

    /// <summary>
    /// Emergency messages re-alert every minute until acknowledged, for up to an hour.
    /// Pushover rejects Emergency messages without these.
    /// </summary>
    private const int EmergencyRetrySeconds = 60;
    private const int EmergencyExpireSeconds = 60 * 60;

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<PushoverChannel> _logger = logger;

    private readonly string _pushoverUser = configuration.GetValue<string>(PushoverUserKey)
        ?? throw new InvalidOperationException($"Pushover value ({PushoverUserKey}) is not configured.");

    private readonly string _pushoverToken = configuration.GetValue<string>(PushoverTokenKey)
        ?? throw new InvalidOperationException($"Pushover value ({PushoverTokenKey}) is not configured.");

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<bool> SendAsync(Notification notification, Site site, CancellationToken cancellationToken)
    {
        var priority = GetPriority(notification.Settings, site.PushoverConfig);
        if (priority == null)
        {
            _logger.LogDebug("Pushover is off for {Kind} notifications for site {SiteId}.", notification.Kind, site.Id);
            return false;
        }

        // Good news shouldn't need acknowledging.
        var isRecovery = notification.Kind is NotificationKind.Recovered or NotificationKind.RecoveredAndUpdated;
        if (isRecovery && priority == PushoverPriority.Emergency)
        {
            priority = PushoverPriority.High;
        }
        var isEmergency = priority == PushoverPriority.Emergency;

        await SendMessageAsync(new PushoverContents
        {
            Title = notification.Title,
            Message = notification.Body,
            Priority = (int)priority,
            Url = notification.SiteLink,
            Attachment = notification.Screenshot,
            Retry = isEmergency ? EmergencyRetrySeconds : null,
            Expire = isEmergency ? EmergencyExpireSeconds : null,
        }, cancellationToken);
        return true;
    }

    private static PushoverPriority? GetPriority(NotificationSettings settings, PushoverConfig config) => settings switch
    {
        NotificationSettings.Success => config.SuccessPriority,
        NotificationSettings.Failure => config.FailurePriority,
        NotificationSettings.FailureThenSuccess => config.FailurePriority ?? config.SuccessPriority,
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings, null),
    };

    private async Task SendMessageAsync(
        PushoverContents contents,
        CancellationToken cancellationToken)
    {
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StringContent(_pushoverUser), "user");
        formContent.Add(new StringContent(_pushoverToken), "token");

        _logger.LogTrace("Building Pushover message to send...");
        if (contents.Attachment?.Length > MaxAttachmentSize)
        {
            _logger.LogWarning("Pushover attachment is too large: {Size} bytes (Max 5MB). Attachment will be omitted.", contents.Attachment.Length);
            contents.Message += "\n\n[Attachment omitted: exceeds 5MB size limit]";
        }
        else if (contents.Attachment?.Length > 0)
        {
            var imageContent = new ByteArrayContent(contents.Attachment);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            formContent.Add(imageContent, "attachment", "screenshot.png");
        }

        using (var stream = new MemoryStream())
        {
            await JsonSerializer.SerializeAsync(stream, contents, _jsonOptions, cancellationToken);
            stream.Position = 0;

            var props = await JsonSerializer.DeserializeAsync<Dictionary<string, object?>>(stream, _jsonOptions, cancellationToken);
            if (props is not null)
            {
                foreach (var (key, value) in props)
                {
                    if (key == "attachment" || value == null)
                    {
                        continue;
                    }

                    formContent.Add(new StringContent(value.ToString()!), key);
                }
            }
        }

        _logger.LogTrace("Sending Pushover message...");
        using var result = await _httpClient.PostAsync("/1/messages.json", formContent, cancellationToken);
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogTrace("Pushover message send failed with status code {StatusCode}. Getting error message...", result.StatusCode);
            var error = await result.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Pushover rejected the message ({(int)result.StatusCode}): {error}",
                inner: null,
                statusCode: result.StatusCode);
        }
    }
}

public static class PushoverChannelExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPushoverChannel()
        {
            services.AddHttpClient<PushoverChannel>((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                var baseAddress = config["PUSHOVER_API_URL"] ?? "https://api.pushover.net";
                httpClient.BaseAddress = new Uri(baseAddress);
                httpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
            });
            return services.AddTransient<INotificationChannel>(sp => sp.GetRequiredService<PushoverChannel>());
        }

        public bool TryAddPushoverChannel(IConfiguration configuration, ILogger? logger = null)
        {
            var user = configuration.GetValue<string>(PushoverChannel.PushoverUserKey);
            var token = configuration.GetValue<string>(PushoverChannel.PushoverTokenKey);
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(token))
            {
                logger?.LogWarning($"Missing pushover configuration: {PushoverChannel.PushoverUserKey} or {PushoverChannel.PushoverTokenKey}. Skipping Pushover channel setup.");
                return false;
            }

            services.AddPushoverChannel();
            return true;
        }
    }
}
