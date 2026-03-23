using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace SiteChecker.Backend.Notifiers.Discord;

public class DiscordService(
    RestClient restClient,
    ILogger<DiscordService> logger)
{
    public const string DiscordTokenKey = "DISCORD_TOKEN";

    private readonly RestClient _restClient = restClient;
    private readonly ILogger<DiscordService> _logger = logger;

    public async Task SendMessageAsync(
        EmbedProperties messageProps,
        ulong channelId,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Sending Discord message to channel {DiscordChannelId}", channelId);
        }

        // Fix URL in case there's non-encoded characters (e.g. spaces)
        if (Uri.TryCreate(messageProps.Url, UriKind.Absolute, out var uri))
        {
            messageProps.Url = uri.AbsoluteUri;
        }

        await _restClient.SendMessageAsync(
            channelId: channelId,
            message: new MessageProperties() { Embeds = [messageProps] },
            cancellationToken: cancellationToken);
    }
}

public static class DiscordServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDiscordService()
        {
            return services
                .AddDiscordGateway((options, serviceProvier) =>
                {
                    var config = serviceProvier.GetRequiredService<IConfiguration>();
                    var token = config[DiscordService.DiscordTokenKey]
                        ?? throw new InvalidOperationException($"Discord token ({DiscordService.DiscordTokenKey}) is not configured.");
                    options.Token = token;
                })
                .AddSingleton<DiscordService>();
        }

        public bool TryAddDiscordService(IConfiguration configuration)
        {
            var token = configuration[DiscordService.DiscordTokenKey];
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            services.AddDiscordService();
            return true;
        }
    }
}
