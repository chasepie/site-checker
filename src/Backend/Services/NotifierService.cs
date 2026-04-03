using System.Diagnostics.CodeAnalysis;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Notifiers.Discord;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Backend.Services;

public class NotifierService(
    ILogger<NotifierService> logger,
    PushoverService? pushoverService = null,
    DiscordService? discordService = null)
    : INotificationService
{
    private readonly ILogger<NotifierService> _logger = logger;
    private readonly PushoverService? _pushoverService = pushoverService;
    private readonly DiscordService? _discordService = discordService;

    public async Task NotifyAsync(
        SiteCheck siteCheck,
        Site site,
        SiteCheckScreenshot? screenshot,
        CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            TrySendPushoverMessageAsync(site, siteCheck, screenshot, cancellationToken),
            TrySendDiscordMessageAsync(site, siteCheck, cancellationToken));
    }

    [MemberNotNullWhen(true, nameof(_pushoverService))]
    private bool CanSendPushoverMessage(Site site, bool isSuccess)
    {
        const string prefix = "Skipping Pushover notification:";

        if (_pushoverService == null)
        {
            _logger.LogWarning($"{prefix} Pushover service is not configured.");
            return false;
        }

        var config = site.PushoverConfig;
        if (isSuccess && config.SuccessPriority == null)
        {
            _logger.LogWarning($"{prefix} Pushover notifications are disabled for successful checks for site {{SiteId}}.", site.Id);
            return false;
        }

        if (!isSuccess && config.FailurePriority == null)
        {
            _logger.LogWarning($"{prefix} Pushover notifications are disabled for failed checks for site {{SiteId}}.", site.Id);
            return false;
        }

        return true;
    }

    private async Task TrySendPushoverMessageAsync(
        Site site,
        SiteCheck siteCheck,
        SiteCheckScreenshot? screenshot,
        CancellationToken cancellationToken)
    {
        if (!CanSendPushoverMessage(site, siteCheck.IsSuccess))
        {
            return;
        }

        _logger.LogTrace("Sending Pushover notification for site {SiteId}", site.Id);
        try
        {
            await _pushoverService.SendMessageAsync(siteCheck.ToPushoverContents(site, screenshot)!, cancellationToken);
            _logger.LogTrace("Pushover notification sent for site {SiteId}", site.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Pushover notification for site {SiteId}", site.Id);
        }
    }

    [MemberNotNullWhen(true, nameof(_discordService))]
    private bool CanSendDiscordMessage(Site site, bool isSuccess)
    {
        const string prefix = "Skipping Discord notification:";

        if (_discordService == null)
        {
            _logger.LogWarning($"{prefix} Discord service is not configured.");
            return false;
        }

        var config = site.DiscordConfig;
        if (config.ChannelId == null)
        {
            _logger.LogWarning($"{prefix} Discord channel ID is not set for site {{SiteId}}.", site.Id);
            return false;
        }

        if (isSuccess && !config.SuccessEnabled)
        {
            _logger.LogWarning($"{prefix} Discord notifications are disabled for successful checks for site {{SiteId}}.", site.Id);
            return false;
        }

        if (!isSuccess && !config.FailureEnabled)
        {
            _logger.LogWarning($"{prefix} Discord notifications are disabled for failed checks for site {{SiteId}}.", site.Id);
            return false;
        }

        return true;
    }

    private async Task TrySendDiscordMessageAsync(
        Site site,
        SiteCheck siteCheck,
        CancellationToken cancellationToken)
    {
        if (!CanSendDiscordMessage(site, siteCheck.IsSuccess))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(_discordService);
        _logger.LogTrace("Sending Discord notification for site {SiteId}", site.Id);
        try
        {
            await _discordService.SendMessageAsync(
                siteCheck.ToDiscordEmbed(site),
                site.DiscordConfig.ChannelId!.Value,
                cancellationToken);
            _logger.LogTrace("Discord notification sent for site {SiteId}", site.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord notification for site {SiteId}", site.Id);
        }
    }
}

public static class NotifierServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNotifierService()
        {
            return services.AddScoped<INotificationService, NotifierService>();
        }
    }
}

