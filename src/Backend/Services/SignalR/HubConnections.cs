using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace SiteChecker.Backend.Services.SignalR;

/// <summary>
/// The open <see cref="DataHub"/> connections. SignalR keeps the user a connection started with, so
/// a logout doesn't end the connections already open; <see cref="CloseSession"/> does. A browser
/// that's logged out gets 401 when it tries to reconnect, and goes to the login page.
/// </summary>
public sealed class HubConnections
{
    /// <summary>
    /// The session claim holding a random id for each login, so a logout closes only the
    /// connections opened with it.
    /// </summary>
    public const string SessionIdClaim = "session-id";

    private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new();

    public void Add(HubCallerContext context) => _connections[context.ConnectionId] = context;

    public void Remove(HubCallerContext context) => _connections.TryRemove(context.ConnectionId, out _);

    /// <summary>
    /// Closes the open connections of the login <paramref name="sessionId"/>: every tab of the
    /// browser that logged out. Other logins stay connected.
    /// </summary>
    public void CloseSession(string sessionId)
    {
        foreach (var context in _connections.Values)
        {
            if (context.User?.FindFirst(SessionIdClaim)?.Value == sessionId)
            {
                context.Abort();
            }
        }
    }
}
