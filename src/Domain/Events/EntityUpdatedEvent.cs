using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Events;

public sealed record EntityUpdatedEvent<TEntity>(TEntity OldEntity, TEntity NewEntity) : IDomainEvent
    where TEntity : IEntityWithId;
