using Microsoft.AspNetCore.SignalR;

namespace SiteChecker.Backend.Services.SignalR;

public class DataHub : Hub
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        return base.OnDisconnectedAsync(exception);
    }
}
