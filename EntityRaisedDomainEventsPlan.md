# Entity-Raised Domain Events Plan

## Problem

Domain events are currently raised by the **repositories** (infrastructure), not by the **entities** (domain). This has several consequences:

1. **Infrastructure decides domain intent.** Repositories generically emit `EntityCreatedEvent`/`EntityUpdatedEvent`/`EntityDeletedEvent` on every save. They don't know *why* the entity changed — a status transition to `Checking` (interim progress) generates the same event type as a transition to `Done` (completion). Handlers must reverse-engineer intent from state diffs.

2. **Cascading events via mutation in handlers.** `QueueEventHandler` handles `EntityCreatedEvent<SiteCheck>`, mutates the entity's `Status` to `Queued`, then calls `UpdateAsync` — which triggers a new `EntityUpdatedEvent` from inside the first event's dispatch. This works but is implicit and fragile.

3. **Shared mutable reference across handlers.** The repository dispatches events with a reference to the same entity object. `QueueEventHandler` mutates `Status` on that reference before other handlers for the same `EntityCreatedEvent` run. Handler ordering becomes load-bearing.

4. **`UpdateAsync` loads the old entity with a separate query.** Every `UpdateAsync` does an extra `AsNoTracking` query just to populate `EntityUpdatedEvent(old, new)`. With entity-raised events, the entity knows what happened — no need to reconstruct the "before" state.

5. **Bulk operations silently skip events.** `RemoveAllForSiteAsync` uses `ExecuteDeleteAsync` and dispatches nothing — SignalR clients aren't notified.

## Solution: Entity-Raised Domain Events

Entities collect domain events internally. Repositories flush them after `SaveChangesAsync`. Events become **semantic** (what happened in the domain) rather than **generic** (what CRUD operation occurred).

---

## Step 1: Add Domain Event Collection to Entities

**Goal:** Give entities the ability to raise domain events.

**Create `src/Domain/Entities/Entity.cs`:**
```csharp
namespace SiteChecker.Domain.Entities;

public abstract class Entity : IEntityWithId
{
    public int Id { get; set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents()
        => _domainEvents.Clear();
}
```

**Modify:**
- `Site` — extend `Entity` instead of implementing `IEntityWithId` directly; remove `public int Id { get; set; }`
- `SiteCheck` — extend `Entity` instead of implementing `IEntityWithId` directly; remove `public required int Id { get; set; }`
- `SiteCheckScreenshot` — extend `Entity` (or keep `IEntityWithId` — it doesn't raise events today, either is fine)
- `IEntityWithId` — keep as-is (Entity implements it); alternatively, merge into Entity if `IEntityWithId` is only used on entities

**Note on EF Core:** EF Core can work with a base `Entity` class. The `_domainEvents` list is not mapped (no backing column) — EF Core ignores fields that don't have corresponding properties unless explicitly configured. Add `.Ignore(e => e.DomainEvents)` in `OnModelCreating` if needed.

**Verify:** `dotnet build`, `dotnet test` — no behavior changes yet.

---

## Step 2: Define Semantic Domain Events

**Goal:** Replace the generic CRUD events with events that express domain intent.

**Create in `src/Domain/Events/`:**
```
SiteCheckCreatedEvent.cs        — raised when a new SiteCheck is created
SiteCheckQueuedEvent.cs         — raised when status transitions to Queued
SiteCheckStatusChangedEvent.cs  — raised for interim status changes (e.g. Checking) so SignalR can push progress
SiteCheckCompletedEvent.cs      — raised when a check reaches Done or Failed
SiteCheckDeletedEvent.cs        — raised when a SiteCheck is removed
SiteUpdatedEvent.cs             — raised when a Site is modified
SiteDeletedEvent.cs             — raised when a Site is removed
```

```csharp
namespace SiteChecker.Domain.Events;

public sealed record SiteCheckCreatedEvent(SiteCheck SiteCheck) : IDomainEvent;
public sealed record SiteCheckQueuedEvent(SiteCheck SiteCheck) : IDomainEvent;
public sealed record SiteCheckStatusChangedEvent(SiteCheck SiteCheck) : IDomainEvent;
public sealed record SiteCheckCompletedEvent(SiteCheck SiteCheck) : IDomainEvent;
public sealed record SiteCheckDeletedEvent(SiteCheck SiteCheck) : IDomainEvent;
public sealed record SiteUpdatedEvent(Site Site) : IDomainEvent;
public sealed record SiteDeletedEvent(Site Site) : IDomainEvent;
```

**Key insight:** `SiteCheckCompletedEvent` replaces the need for `EntityUpdatedEvent` + checking `wasAlreadyComplete` in `NotificationEventHandler`. The event itself *means* "the check just completed" — no state diffing needed.

**Delete** `EntityCreatedEvent.cs`, `EntityUpdatedEvent.cs`, `EntityDeletedEvent.cs` — they are fully replaced.

**Verify:** `dotnet build` — events are just records, nothing uses them yet.

---

## Step 3: Raise Events from Entity Methods

**Goal:** Entity state transitions raise the appropriate domain events.

**Modify `SiteCheck`:**
- Constructor `SiteCheck(int siteId)` — raise `SiteCheckCreatedEvent(this)` at end
- New method `Enqueue()` — sets `Status = CheckStatus.Queued`, raises `SiteCheckQueuedEvent(this)`. Replaces the bare `siteCheck.Status = CheckStatus.Queued` in `QueueEventHandler`
- New method `BeginChecking(string? vpnLocationId)` — sets `Status = CheckStatus.Checking` and `VpnLocationId`, raises `SiteCheckStatusChangedEvent(this)`. Replaces the bare property assignments in `PerformSiteCheckUseCase`
- `CompleteWithResult(IScrapeResult)` — raise `SiteCheckCompletedEvent(this)` at end
- `FailWithException(Exception)` — raise `SiteCheckCompletedEvent(this)` at end

**Modify `Site`:**
- `Update(SiteUpdate)` — raise `SiteUpdatedEvent(this)` at end

**Note:** `SiteCheckStatusChangedEvent` is a lightweight event consumed only by SignalR for real-time progress. The more specific `SiteCheckCompletedEvent` and `SiteCheckQueuedEvent` drive business logic (notifications, queue).

**Verify:** `dotnet build`, `dotnet test` — entities raise events but nobody collects them yet.

---

## Step 4: Flush Events from Repositories

**Goal:** Repositories collect and dispatch entity events after `SaveChangesAsync`, replacing the manual event construction.

**Modify `SiteCheckRepository`:**
```csharp
public async Task AddAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default)
{
    dbContext.SiteChecks.Add(siteCheck);
    await dbContext.SaveChangesAsync(cancellationToken);
    await DispatchAndClearEvents(siteCheck, cancellationToken);
}

public async Task UpdateAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default)
{
    dbContext.SiteChecks.Update(siteCheck);
    await dbContext.SaveChangesAsync(cancellationToken);
    await DispatchAndClearEvents(siteCheck, cancellationToken);
}

private async Task DispatchAndClearEvents(Entity entity, CancellationToken cancellationToken)
{
    foreach (var domainEvent in entity.DomainEvents)
    {
        await dispatcher.DispatchAsync(domainEvent, cancellationToken);
    }
    entity.ClearDomainEvents();
}
```

**Key change:** `UpdateAsync` no longer does the extra `AsNoTracking` query to load the old entity. The events already carry the right information.

**Apply the same pattern to `SiteRepository`.**

**Consider:** Extract `DispatchAndClearEvents` to a base repository class or a shared helper to avoid duplication.

**Verify:** `dotnet build` — events are now raised and dispatched, but handlers still expect the old generic events.

---

## Step 5: Migrate Event Handlers to Semantic Events

**Goal:** Update handlers to subscribe to semantic events instead of generic CRUD events.

### QueueEventHandler
**Before:** Handles `EntityCreatedEvent<SiteCheck>`, checks status, mutates entity, calls `UpdateAsync`.
**After:** Handles `SiteCheckCreatedEvent`. Calls `siteCheck.Enqueue()` then `siteCheckRepository.UpdateAsync()`. No status check needed — the event already means "a check was created", and `Enqueue()` encapsulates the transition. The `SiteCheckQueuedEvent` raised by `Enqueue()` will be dispatched when `UpdateAsync` flushes events.

```csharp
public class QueueEventHandler(ISiteCheckQueue queue, ISiteCheckRepository siteCheckRepository)
    : IDomainEventHandler<SiteCheckCreatedEvent>
{
    public async Task HandleAsync(SiteCheckCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var siteCheck = domainEvent.SiteCheck;
        siteCheck.Enqueue();
        await siteCheckRepository.UpdateAsync(siteCheck, cancellationToken);
        await queue.QueueCheckAsync(siteCheck, cancellationToken);
    }
}
```

### NotificationEventHandler
**Before:** Handles `EntityUpdatedEvent<SiteCheck>`, passes `wasAlreadyComplete` derived from `OldEntity.IsComplete`.
**After:** Handles `SiteCheckCompletedEvent`. The event itself means "check just completed" — no need for `wasAlreadyComplete`.

```csharp
public class NotificationEventHandler(NotifyCheckCompletedUseCase useCase)
    : IDomainEventHandler<SiteCheckCompletedEvent>
{
    public async Task HandleAsync(SiteCheckCompletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await useCase.ExecuteAsync(domainEvent.SiteCheck.Id, cancellationToken);
    }
}
```

**Simplify `NotifyCheckCompletedUseCase`:** Remove the `wasAlreadyComplete` parameter — the event guarantees this is a fresh completion.

### SignalREventHandler

Replace the generic `SignalREventHandler<TEntity>` with explicit handlers per semantic event. With only 2 entity types and ~6 events, this is straightforward and keeps everything explicit.

**Delete** `SignalREventHandler.cs` (the generic one).

**Create `src/Backend/Services/DomainEvents/SignalRSiteCheckEventHandler.cs`:**
```csharp
public class SignalRSiteCheckEventHandler(IHubContext<DataHub> hubContext, IHttpContextAccessor httpContextAccessor)
    : IDomainEventHandler<SiteCheckCreatedEvent>,
      IDomainEventHandler<SiteCheckQueuedEvent>,
      IDomainEventHandler<SiteCheckCompletedEvent>,
      IDomainEventHandler<SiteCheckStatusChangedEvent>,
      IDomainEventHandler<SiteCheckDeletedEvent>
{
    public async Task HandleAsync(SiteCheckCreatedEvent domainEvent, CancellationToken cancellationToken = default)
        => await SendAsync(SignalRConstants.OnEntityCreatedKey, new
        {
            EntityTypeName = nameof(SiteCheck),
            EntityId = domainEvent.SiteCheck.Id,
            Entity = domainEvent.SiteCheck,
        }, cancellationToken);

    public async Task HandleAsync(SiteCheckQueuedEvent domainEvent, CancellationToken cancellationToken = default)
        => await SendUpdated(domainEvent.SiteCheck, cancellationToken);

    public async Task HandleAsync(SiteCheckCompletedEvent domainEvent, CancellationToken cancellationToken = default)
        => await SendUpdated(domainEvent.SiteCheck, cancellationToken);

    public async Task HandleAsync(SiteCheckStatusChangedEvent domainEvent, CancellationToken cancellationToken = default)
        => await SendUpdated(domainEvent.SiteCheck, cancellationToken);

    public async Task HandleAsync(SiteCheckDeletedEvent domainEvent, CancellationToken cancellationToken = default)
        => await SendAsync(SignalRConstants.OnEntityDeletedKey, new
        {
            EntityTypeName = nameof(SiteCheck),
            EntityId = domainEvent.SiteCheck.Id,
        }, cancellationToken);

    private async Task SendUpdated(SiteCheck siteCheck, CancellationToken cancellationToken)
        => await SendAsync(SignalRConstants.OnEntityUpdatedKey, new
        {
            EntityTypeName = nameof(SiteCheck),
            EntityId = siteCheck.Id,
            NewEntity = siteCheck,
        }, cancellationToken);

    private async Task SendAsync(string method, object payload, CancellationToken cancellationToken)
        => await GetClients().SendAsync(method, payload, cancellationToken);

    // GetClients() — same logic as current SignalREventHandler (exclude requesting connection)
}
```

**Create `src/Backend/Services/DomainEvents/SignalRSiteEventHandler.cs`:**
Same pattern for `SiteUpdatedEvent`, `SiteDeletedEvent` (and `SiteCreatedEvent` if you add one later).

**Note on `OldEntity`:** The current SignalR `EntityUpdatedEvent` payload includes `OldEntity`, and the frontend Zod schema (`signalr-types.ts`) defines an `oldEntity` field on `UpdatedEntityChange`. However, `oldEntity` is **never read** anywhere in the frontend code — stores upsert from `newEntity` only. Safe to drop. Update `UpdatedEntityChange` in `signalr-types.ts` to remove the `oldEntity` field.

**Verify:** Full end-to-end — create check, watch it queue, process, complete, verify SignalR + notifications.

---

## Step 6: Clean Up

- **Delete** `EntityCreatedEvent<T>`, `EntityUpdatedEvent<T>`, `EntityDeletedEvent<T>`
- **Delete** generic `SignalREventHandler.cs`
- **Delete** empty `src/Database/Services/` directory
- **Update DI registrations** in `Program.cs` — remove old generic handler registrations, add new semantic handlers
- **Update frontend** `signalr-types.ts` — remove `oldEntity` from `UpdatedEntityChange`
- **Update tests** in `test/Application.Test/` — `NotifyCheckCompletedUseCase` no longer takes `wasAlreadyComplete`
- **Update tests** in `test/Domain.Test/` — verify `SiteCheck.CompleteWithResult` raises `SiteCheckCompletedEvent`, `Enqueue()` raises `SiteCheckQueuedEvent`, `BeginChecking()` raises `SiteCheckStatusChangedEvent`, etc.
- **Optionally split** `ManageSitesUseCase` into `ManageSitesUseCase` + `ManageSiteChecksUseCase` (low priority, independent of events)

**Verify:** `dotnet build`, `dotnet test`, full manual end-to-end.

---

## Summary of Changes by Layer

| Layer           | Changes                                                                                                                                                                                                          |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Domain**      | `Entity` base class with event collection; 7 semantic event records; `SiteCheck.Enqueue()` + `BeginChecking()` methods; events raised from `CompleteWithResult`, `FailWithException`, `Enqueue`, `BeginChecking`, `Site.Update` |
| **Application** | `NotifyCheckCompletedUseCase` drops `wasAlreadyComplete` parameter                                                                                                                                               |
| **Database**    | Repositories flush entity events instead of constructing generic events; remove extra `AsNoTracking` query from `UpdateAsync`                                                                                    |
| **Backend**     | Explicit SignalR handlers per entity type; semantic event subscriptions for Queue/Notification handlers; DI registrations updated                                                                                 |
| **Frontend**    | Remove `oldEntity` from `UpdatedEntityChange` in `signalr-types.ts`                                                                                                                                             |

## Key Risks

| Risk                                                               | Mitigation                                                                                       |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| EF Core tries to map `DomainEvents` / `Entity` base class          | `.Ignore(e => e.DomainEvents)` in `OnModelCreating`; verify empty migration                      |
| Event ordering changes break behavior                              | `QueueEventHandler` currently relies on cascade; with `Enqueue()` the flow is explicit           |
| SignalR misses updates during transition                           | Migrate SignalR handler last; keep generic events until it's done                                |
| `SiteCheck(int siteId)` constructor raises event before `AddAsync` | This is fine — the event is collected, not dispatched; it fires only when the repository flushes |
