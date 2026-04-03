using SiteChecker.Domain.Events;

namespace SiteChecker.Domain.Ports;

public interface IDomainEventHandler<TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
