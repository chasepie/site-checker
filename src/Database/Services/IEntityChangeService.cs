namespace SiteChecker.Database.Services;

public interface IEntityChange
{
    public string EntityTypeName { get; set; }
    public int EntityId { get; set; }
}

public abstract class EntityChange : IEntityChange
{
    public required string EntityTypeName { get; set; }
    public required int EntityId { get; set; }
}

public class CreatedEntityChange : EntityChange
{
    public required object Entity { get; set; }
}

public class UpdatedEntityChange : EntityChange
{
    public required object OldEntity { get; set; }
    public required object NewEntity { get; set; }
}

public class DeletedEntityChange : EntityChange { }

public interface IEntityChangeService
{
    public Task OnEntityCreated(
        CreatedEntityChange change,
        CancellationToken cancellationToken = default);

    public Task OnEntityUpdated(
        UpdatedEntityChange change,
        CancellationToken cancellationToken = default);

    public Task OnEntityDeleted(
        DeletedEntityChange change,
        CancellationToken cancellationToken = default);
}
