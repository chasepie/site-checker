using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiteChecker.Database.Model;
using SiteChecker.Database.Services;

namespace SiteChecker.Database;

public class ChangesInterceptor(
    IEnumerable<IEntityChangeService>? entityUpdateServices = null)
    : SaveChangesInterceptor
{
    private readonly IEnumerable<IEntityChangeService> _entityUpdateServices = entityUpdateServices ?? [];

    private sealed class EntityChanges
    {
        public List<CreatedEntityChange> AddedEntities { get; } = [];
        public List<UpdatedEntityChange> UpdatedEntities { get; } = [];
        public List<DeletedEntityChange> DeletedEntities { get; } = [];
    }
    private EntityChanges? _entityChanges;

    private static EntityChanges GetEntityChanges(DbContextEventData eventData)
    {
        var changes = new EntityChanges();

        var context = eventData.Context;
        if (context == null)
        {
            return changes;
        }

        context.ChangeTracker.DetectChanges();

        foreach (var c in context.ChangeTracker.Entries())
        {
            if (c.Entity is not IEntityWithId entityWithId)
            {
                continue;
            }

            var entityTypeName = c.Metadata.ShortName();
            if (entityTypeName.Equals(nameof(SiteCheckScreenshot), StringComparison.OrdinalIgnoreCase))
            {
                // Don't send updates due to size - have user load manually
                continue;
            }

            if (c.State == EntityState.Added)
            {
                changes.AddedEntities.Add(new()
                {
                    EntityTypeName = entityTypeName,
                    EntityId = entityWithId.Id,
                    Entity = c.Entity
                });
            }
            else if (c.State == EntityState.Modified)
            {
                changes.UpdatedEntities.Add(new()
                {
                    EntityTypeName = entityTypeName,
                    EntityId = entityWithId.Id,
                    OldEntity = c.OriginalValues.ToObject()!,
                    NewEntity = c.Entity
                });
            }
            else if (c.State == EntityState.Deleted)
            {
                changes.DeletedEntities.Add(new()
                {
                    EntityTypeName = entityTypeName,
                    EntityId = entityWithId.Id,
                });
            }
        }

        return changes;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (_entityUpdateServices.Any())
        {
            _entityChanges = GetEntityChanges(eventData);
        }
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_entityUpdateServices.Any())
        {
            _entityChanges = GetEntityChanges(eventData);
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task SendUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_entityChanges == null)
        {
            return;
        }

        var serviceTasks = _entityUpdateServices.Select(async service =>
        {
            foreach (var added in _entityChanges.AddedEntities)
            {
                if (added.Entity is IEntityWithId entity)
                {
                    added.EntityId = entity.Id;
                }
                await service.OnEntityCreated(added, cancellationToken);
            }

            foreach (var updated in _entityChanges.UpdatedEntities)
            {
                await service.OnEntityUpdated(updated, cancellationToken);
            }

            foreach (var deleted in _entityChanges.DeletedEntities)
            {
                await service.OnEntityDeleted(deleted, cancellationToken);
            }
        });

        await Task.WhenAll(serviceTasks);
    }

    private void SendUpdates()
    {
        SendUpdatesAsync().GetAwaiter().GetResult();
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        SendUpdates();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await SendUpdatesAsync(cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
