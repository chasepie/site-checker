using System.Diagnostics.CodeAnalysis;
using SiteChecker.Application.UseCases;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Events;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Backend.Services.DomainEvents;

/// <summary>
/// Triggers notification logic when a SiteCheck is updated. Delegates to
/// <see cref="NotifyCheckCompletedUseCase"/> to determine whether a notification should be sent.
/// Replaces the NotifierService.OnEntityUpdated logic from the old IEntityChangeService flow.
/// </summary>
[SuppressMessage("Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix",
    Justification = "EventHandler is the naming convention for domain event handler implementations.")]
public class NotificationEventHandler(NotifyCheckCompletedUseCase useCase)
    : IDomainEventHandler<EntityUpdatedEvent<SiteCheck>>
{
    public async Task HandleAsync(EntityUpdatedEvent<SiteCheck> domainEvent, CancellationToken cancellationToken = default)
    {
        await useCase.ExecuteAsync(
            domainEvent.NewEntity.Id,
            wasAlreadyComplete: domainEvent.OldEntity.IsComplete,
            cancellationToken);
    }
}
