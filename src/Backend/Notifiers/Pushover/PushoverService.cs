using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiteChecker.Backend.Notifiers.Pushover;

public class PushoverService
{
    public const string PushoverUserKey = "PUSHOVER_USER";
    public const string PushoverTokenKey = "PUSHOVER_TOKEN";
    public const int MaxAttachmentSize = 5 * 1024 * 1024; // 5 MB

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PushoverService> _logger;
    private readonly string _pushoverUser;
    private readonly string _pushoverToken;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PushoverService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PushoverService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _pushoverUser = _configuration.GetValue<string>(PushoverUserKey)
            ?? throw new InvalidOperationException($"Pushover value ({PushoverUserKey}) is not configured.");

        _pushoverToken = _configuration.GetValue<string>(PushoverTokenKey)
            ?? throw new InvalidOperationException($"Pushover value ({PushoverTokenKey}) is not configured.");
    }

    public async Task SendMessageAsync(
        PushoverContents contents,
        CancellationToken cancellationToken = default)
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
            _logger.LogError("Failed to send Pushover message (StatusCode: {StatusCode}): {Error}", result.StatusCode, error);
        }
    }
}

public static class PushoverServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IHttpClientBuilder AddPushoverService()
        {
            return services.AddHttpClient<PushoverService>((serviceProvider, httpClient) =>
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                var baseAddress = config["PUSHOVER_API_URL"] ?? "https://api.pushover.net";
                httpClient.BaseAddress = new Uri(baseAddress);
                httpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
            });
        }

        public bool TryAddPushoverService(IConfiguration configuration, ILogger? logger = null)
        {
            var user = configuration.GetValue<string>(PushoverService.PushoverUserKey);
            var token = configuration.GetValue<string>(PushoverService.PushoverTokenKey);
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(token))
            {
                logger?.LogWarning($"Missing pushover configuration: {PushoverService.PushoverUserKey} or {PushoverService.PushoverTokenKey}. Skipping Pushover notifier setup.");
                return false;
            }

            services.AddPushoverService();
            return true;
        }
    }
}
