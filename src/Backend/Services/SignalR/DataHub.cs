using Microsoft.AspNetCore.SignalR;

namespace SiteChecker.Backend.Services.SignalR;

public sealed class DataHub(HubConnections connections) : Hub
{
    private readonly HubConnections _connections = connections;

    public override Task OnConnectedAsync()
    {
        _connections.Add(Context);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context);
        return base.OnDisconnectedAsync(exception);
    }
}
