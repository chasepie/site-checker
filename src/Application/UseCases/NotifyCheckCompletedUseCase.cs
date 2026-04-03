using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Application.UseCases;

/// <summary>
/// Determines whether a completed site check should trigger notifications and, if so, invokes
/// <see cref="INotificationService"/>. Extracted from NotifierService.OnEntityUpdated.
/// </summary>
public class NotifyCheckCompletedUseCase(
    ISiteRepository siteRepository,
    ISiteCheckRepository siteCheckRepository,
    INotificationService notificationService)
{
    private readonly ISiteRepository _siteRepository = siteRepository;
    private readonly ISiteCheckRepository _siteCheckRepository = siteCheckRepository;
    private readonly INotificationService _notificationService = notificationService;

    /// <summary>
    /// Evaluates notification rules for a site check and sends a notification if warranted.
    /// </summary>
    /// <param name="siteCheckId">ID of the check that was just updated.</param>
    /// <param name="wasAlreadyComplete">
    /// True if the check was already in a terminal state before this update. When true, no
    /// notification is sent (avoids duplicate notifications on re-saves).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteAsync(
        int siteCheckId,
        bool wasAlreadyComplete,
        CancellationToken cancellationToken = default)
    {
        var siteCheck = await _siteCheckRepository.GetByIdAsync(siteCheckId, cancellationToken);
        if (siteCheck == null)
        {
            return;
        }

        // Only notify on the incomplete → complete transition.
        if (wasAlreadyComplete || !siteCheck.IsComplete)
        {
            return;
        }

        if (siteCheck.Status == CheckStatus.Done)
        {
            var previousCheck = await _siteCheckRepository.GetPreviousSuccessfulAsync(
                siteCheck.SiteId, siteCheck.Id, cancellationToken);

            // No previous successful check means there is no baseline to compare against.
            if (previousCheck == null)
            {
                return;
            }

            // Content unchanged — not worth notifying.
            if (previousCheck.Value?.Equals(siteCheck.Value, StringComparison.Ordinal) == true)
            {
                return;
            }
        }
        else if (siteCheck.Status == CheckStatus.Failed && siteCheck.IsKnownFailure)
        {
            return;
        }

        var site = await _siteRepository.GetByIdAsync(siteCheck.SiteId, cancellationToken);
        if (site == null)
        {
            return;
        }

        var screenshot = await _siteCheckRepository.GetScreenshotAsync(siteCheck.Id, cancellationToken);
        await _notificationService.NotifyAsync(siteCheck, site, screenshot, cancellationToken);
    }
}
