using Microsoft.AspNetCore.SignalR;
using SiteChecker.Database.Services;

namespace SiteChecker.Backend.Services.SignalR;

public class EntityChangesService(
    IHubContext<DataHub> hubContext,
    IHttpContextAccessor httpContextAccessor)
    : IEntityChangeService
{
    private readonly IHubContext<DataHub> _hubContext = hubContext;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    private IClientProxy GetClients()
    {
        var clients = _hubContext.Clients;
        var clientProxy = clients.All;

        var httpContext = _httpContextAccessor.HttpContext;
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

    public async Task OnEntityCreated(
        CreatedEntityChange change,
        CancellationToken cancellationToken = default)
    {
        await GetClients().SendAsync(SignalRConstants.OnEntityCreatedKey, change, cancellationToken);
    }

    public async Task OnEntityUpdated(
        UpdatedEntityChange change,
        CancellationToken cancellationToken = default)
    {
        await GetClients().SendAsync(SignalRConstants.OnEntityUpdatedKey, change, cancellationToken);
    }

    public async Task OnEntityDeleted(
        DeletedEntityChange change,
        CancellationToken cancellationToken = default)
    {
        await GetClients().SendAsync(SignalRConstants.OnEntityDeletedKey, change, cancellationToken);
    }
}
