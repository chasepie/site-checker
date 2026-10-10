using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace SiteChecker.Backend.Services.SignalR;

/// <summary>
/// The open <see cref="DataHub"/> connections. SignalR keeps the user a connection started with, so
/// a logout doesn't end the connections already open; <see cref="CloseAll"/> does. Browsers still
/// logged in reconnect on their own; one that's logged out gets 401 and goes to the login page.
/// </summary>
public sealed class HubConnections
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new();

    public void Add(HubCallerContext context) => _connections[context.ConnectionId] = context;

    public void Remove(HubCallerContext context) => _connections.TryRemove(context.ConnectionId, out _);

    /// <summary>
    /// Closes every open connection. There's one user, so a logout anywhere closes them all.
    /// </summary>
    public void CloseAll()
    {
        foreach (var context in _connections.Values)
        {
            context.Abort();
        }
    }
}
