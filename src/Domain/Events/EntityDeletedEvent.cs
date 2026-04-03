using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Events;

public sealed record EntityDeletedEvent<TEntity>(TEntity Entity) : IDomainEvent
    where TEntity : IEntityWithId;
