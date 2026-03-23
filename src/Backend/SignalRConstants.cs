using SiteChecker.Database.Services;

namespace SiteChecker.Backend;

public class SignalRConstants
{
    public const string OnEntityCreatedKey = nameof(IEntityChangeService.OnEntityCreated);
    public const string OnEntityUpdatedKey = nameof(IEntityChangeService.OnEntityUpdated);
    public const string OnEntityDeletedKey = nameof(IEntityChangeService.OnEntityDeleted);

    public const string OnLocationChangedKey = "OnLocationChanged";
    public const string UserID = "SiteChecker-SignalR-Id";
    public const string HubName = "dataHub";
}
