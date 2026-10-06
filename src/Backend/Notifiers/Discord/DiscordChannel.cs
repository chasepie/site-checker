using NetCord.Hosting.Gateway;
using NetCord.Rest;
using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Notifiers.Discord;

/// <summary>
/// Sends notifications as Discord embeds to the channel the Site configures.
/// </summary>
public sealed class DiscordChannel(
    RestClient restClient,
    ILogger<DiscordChannel> logger)
    : INotificationChannel
{
    public const string DiscordTokenKey = "DISCORD_TOKEN";

    private readonly RestClient _restClient = restClient;
    private readonly ILogger<DiscordChannel> _logger = logger;

    public async Task SendAsync(Notification notification, Site site, CancellationToken cancellationToken)
    {
        var config = site.DiscordConfig;
        if (config.ChannelId is not { } channelId || !IsEnabled(notification.Kind, config))
        {
            _logger.LogDebug("Discord is off for {Kind} notifications for site {SiteId}.", notification.Kind, site.Id);
            return;
        }

        var embed = new EmbedProperties
        {
            Title = notification.Title,
            Description = notification.Body,
            Url = notification.SiteLink,
        };

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Sending Discord message to channel {DiscordChannelId}", channelId);
        }

        await _restClient.SendMessageAsync(
            channelId: channelId,
            message: new MessageProperties() { Embeds = [embed] },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Recoveries use the failure setting so the all-clear reaches you wherever the alert did.
    /// A Recovery that also changed content falls back to the success setting, so the content
    /// change isn't lost when failure notifications are off.
    /// </summary>
    private static bool IsEnabled(NotificationKind kind, DiscordConfig config) => kind switch
    {
        NotificationKind.Updated => config.SuccessEnabled,
        NotificationKind.Failing or NotificationKind.Recovered => config.FailureEnabled,
        NotificationKind.RecoveredAndUpdated => config.FailureEnabled || config.SuccessEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

public static class DiscordChannelExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDiscordChannel()
        {
            return services
                .AddDiscordGateway((options, serviceProvider) =>
                {
                    var config = serviceProvider.GetRequiredService<IConfiguration>();
                    var token = config[DiscordChannel.DiscordTokenKey]
                        ?? throw new InvalidOperationException($"Discord token ({DiscordChannel.DiscordTokenKey}) is not configured.");
                    options.Token = token;
                })
                .AddSingleton<DiscordChannel>()
                .AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<DiscordChannel>());
        }

        public bool TryAddDiscordChannel(IConfiguration configuration)
        {
            var token = configuration[DiscordChannel.DiscordTokenKey];
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            services.AddDiscordChannel();
            return true;
        }
    }
}
