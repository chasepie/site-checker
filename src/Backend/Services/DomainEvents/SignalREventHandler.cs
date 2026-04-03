using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Events;
using SiteChecker.Domain.Ports;
using SiteChecker.Backend.Services.SignalR;

namespace SiteChecker.Backend.Services.DomainEvents;

/// <summary>
/// Broadcasts entity lifecycle events (created, updated, deleted) to all SignalR clients,
/// mirroring what EntityChangesService did via IEntityChangeService.
/// </summary>
[SuppressMessage("Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix",
    Justification = "EventHandler is the naming convention for domain event handler implementations.")]
public class SignalREventHandler<TEntity>(
    IHubContext<DataHub> hubContext,
    IHttpContextAccessor httpContextAccessor)
    : IDomainEventHandler<EntityCreatedEvent<TEntity>>,
      IDomainEventHandler<EntityUpdatedEvent<TEntity>>,
      IDomainEventHandler<EntityDeletedEvent<TEntity>>
    where TEntity : class, IEntityWithId
{
    private IClientProxy GetClients()
    {
        var clients = hubContext.Clients;
        var clientProxy = clients.All;

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var requestHeaders = httpContext.Request.Headers;
            if (requestHeaders.TryGetValue(SignalRConstants.UserID, out var connId))
            {
                clientProxy = clients.AllExcept(connId);
            }
        }

        return clientProxy;
    }

    public async Task HandleAsync(EntityCreatedEvent<TEntity> domainEvent, CancellationToken cancellationToken = default)
    {
        await GetClients().SendAsync(SignalRConstants.OnEntityCreatedKey, new
        {
            EntityTypeName = typeof(TEntity).Name,
            EntityId = domainEvent.Entity.Id,
            Entity = domainEvent.Entity,
        }, cancellationToken);
    }

    public async Task HandleAsync(EntityUpdatedEvent<TEntity> domainEvent, CancellationToken cancellationToken = default)
    {
        await GetClients().SendAsync(SignalRConstants.OnEntityUpdatedKey, new
        {
            EntityTypeName = typeof(TEntity).Name,
            EntityId = domainEvent.NewEntity.Id,
            OldEntity = domainEvent.OldEntity,
            NewEntity = domainEvent.NewEntity,
        }, cancellationToken);
    }

    public async Task HandleAsync(EntityDeletedEvent<TEntity> domainEvent, CancellationToken cancellationToken = default)
    {
        await GetClients().SendAsync(SignalRConstants.OnEntityDeletedKey, new
        {
            EntityTypeName = typeof(TEntity).Name,
            EntityId = domainEvent.Entity.Id,
        }, cancellationToken);
    }
}
