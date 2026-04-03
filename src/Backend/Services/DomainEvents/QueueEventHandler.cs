using System.Diagnostics.CodeAnalysis;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Events;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Backend.Services.DomainEvents;

/// <summary>
/// When a new SiteCheck is created with status Created, transitions it to Queued and enqueues it
/// for processing. Replaces the auto-enqueue logic from SiteCheckQueueService.OnEntityCreated.
/// </summary>
[SuppressMessage("Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix",
    Justification = "EventHandler is the naming convention for domain event handler implementations.")]
public class QueueEventHandler(
    ISiteCheckQueue queue,
    ISiteCheckRepository siteCheckRepository)
    : IDomainEventHandler<EntityCreatedEvent<SiteCheck>>
{
    public async Task HandleAsync(EntityCreatedEvent<SiteCheck> domainEvent, CancellationToken cancellationToken = default)
    {
        var siteCheck = domainEvent.Entity;
        if (siteCheck.Status != CheckStatus.Created)
        {
            return;
        }

        siteCheck.Status = CheckStatus.Queued;
        await siteCheckRepository.UpdateAsync(siteCheck, cancellationToken);
        await queue.QueueCheckAsync(siteCheck, cancellationToken);
    }
}
