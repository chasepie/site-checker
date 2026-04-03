using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Events;

public sealed record EntityCreatedEvent<TEntity>(TEntity Entity) : IDomainEvent
    where TEntity : IEntityWithId;
