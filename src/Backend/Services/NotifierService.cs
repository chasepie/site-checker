using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Database.Services;
using SiteChecker.Backend.Notifiers.Discord;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Scraper;

namespace SiteChecker.Backend.Services;

public class NotifierService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotifierService> logger,
    PushoverService? pushoverService = null,
    DiscordService? discordService = null)
    : IEntityChangeService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<NotifierService> _logger = logger;
    private readonly PushoverService? _pushoverService = pushoverService;
    private readonly DiscordService? _discordService = discordService;

    public Task OnEntityCreated(CreatedEntityChange change, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task OnEntityDeleted(DeletedEntityChange change, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task OnEntityUpdated(UpdatedEntityChange change, CancellationToken cancellationToken = default)
    {
        // Confirm that the changed entity is a SiteCheck
        if (change.EntityTypeName != nameof(SiteCheck)
            || change.OldEntity is not SiteCheck oldSiteCheck
            || change.NewEntity is not SiteCheck siteCheck)
        {
            return;
        }

        // Confirm that the IDs match
        if (oldSiteCheck.Id != siteCheck.Id)
        {
            _logger.LogWarning("Mismatched SiteCheck IDs in update notification: {OldId} != {NewId}",
                oldSiteCheck.Id, siteCheck.Id);
            return;
        }

        // If the check was already complete before or is not complete now, do not notify
        if (oldSiteCheck.IsComplete || !siteCheck.IsComplete)
        {
            return;
        }

        await HandleCompletedSiteCheckAsync(siteCheck, cancellationToken);
    }

    private async Task HandleCompletedSiteCheckAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        using var dbContext = scope.ServiceProvider
            .GetRequiredService<SiteCheckerDbContext>();

        if (siteCheck.Status == CheckStatus.Done)
        {
            if (!await IsContentUpdatedAsync(dbContext, siteCheck, cancellationToken))
            {
                return;
            }
        }
        else if (siteCheck.Status == CheckStatus.Failed)
        {
            if (siteCheck.IsKnownExceptionFailure())
            {
                return;
            }
        }
        await SendNotificationsAsync(dbContext, siteCheck, cancellationToken);
    }

    private static async Task<bool> IsContentUpdatedAsync(
        SiteCheckerDbContext dbContext,
        SiteCheck siteCheck,
        CancellationToken cancellationToken = default)
    {
        var previousCheck = await dbContext.SiteChecks
            .Where(sc => sc.SiteId == siteCheck.SiteId
                && sc.Id < siteCheck.Id
                && sc.Status == CheckStatus.Done)
            .OrderByDescending(sc => sc.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // If there is no previous check, do not consider it updated
        if (previousCheck == null)
        {
            return false;
        }

        return previousCheck.Value?.Equals(siteCheck.Value, StringComparison.Ordinal) == false;
    }

    public async Task SendNotificationsAsync(
        SiteCheckerDbContext dbContext,
        SiteCheck siteCheck,
        CancellationToken cancellationToken = default)
    {
        if (siteCheck.Site == null)
        {
            await dbContext
                .Entry(siteCheck)
                .Reference(sc => sc.Site)
                .LoadAsync(cancellationToken);
        }

        if (siteCheck.Site == null)
        {
            _logger.LogWarning("SiteCheck {SiteCheckId} has no associated Site; skipping notifications.", siteCheck.Id);
            return;
        }

        if (siteCheck.Screenshot == null)
        {
            await dbContext
                .Entry(siteCheck)
                .Reference(sc => sc.Screenshot)
                .LoadAsync(cancellationToken);
        }

        await Task.WhenAll(
            TrySendPushoverMessageAsync(siteCheck, cancellationToken),
            TrySendDiscordMessageAsync(siteCheck, cancellationToken));
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
        SiteCheck siteCheck,
        CancellationToken cancellationToken)
    {
        if (!CanSendPushoverMessage(siteCheck.Site, siteCheck.IsSuccess))
        {
            return;
        }

        _logger.LogTrace("Sending Pushover notification for site {SiteId}", siteCheck.Site.Id);
        try
        {
            await _pushoverService.SendMessageAsync(siteCheck.ToPushoverContents()!, cancellationToken);
            _logger.LogTrace("Pushover notification sent for site {SiteId}", siteCheck.Site.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Pushover notification for site {SiteId}", siteCheck.Site.Id);
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
        SiteCheck siteCheck,
        CancellationToken cancellationToken)
    {
        if (!CanSendDiscordMessage(siteCheck.Site, siteCheck.IsSuccess))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(_discordService);
        _logger.LogTrace("Sending Discord notification for site {SiteId}", siteCheck.Site.Id);
        try
        {
            await _discordService.SendMessageAsync(
                siteCheck.ToDiscordEmbed(),
                siteCheck.Site.DiscordConfig.ChannelId!.Value,
                cancellationToken);
            _logger.LogTrace("Discord notification sent for site {SiteId}", siteCheck.Site.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord notification for site {SiteId}", siteCheck.Site.Id);
        }
    }
}

public static class NotifierServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNotifierService()
        {
            return services.AddSingleton<IEntityChangeService, NotifierService>();
        }
    }
}
