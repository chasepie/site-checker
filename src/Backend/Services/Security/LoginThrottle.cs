using System.Net;
using System.Threading.RateLimiting;

namespace SiteChecker.Backend.Services.Security;

/// <summary>
/// Limits login attempts to <see cref="AttemptsPerClient"/> a minute from each address, so another
/// host can't use up yours, and <see cref="AttemptsInAll"/> a minute from all of them, so changing
/// addresses doesn't buy more guesses. The login calls it only once the request can log in (a JSON
/// body), so a web page on another site, which can't send one without CORS, can't use up attempts.
/// Behind a reverse proxy, clients are told apart only if it's in <c>TRUSTED_PROXIES</c>; otherwise
/// they all share the proxy's limit.
/// </summary>
public sealed class LoginThrottle : IDisposable
{
    public const int AttemptsPerClient = 5;

    public const int AttemptsInAll = 30;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly PartitionedRateLimiter<IPAddress> _perClient = PartitionedRateLimiter.Create<IPAddress, IPAddress>(
        address => RateLimitPartition.GetFixedWindowLimiter(address, _ => Limit(AttemptsPerClient)));

    /// <summary>
    /// One partition, shared by every client.
    /// </summary>
    private readonly PartitionedRateLimiter<IPAddress> _inAll = PartitionedRateLimiter.Create<IPAddress, bool>(
        _ => RateLimitPartition.GetFixedWindowLimiter(true, _ => Limit(AttemptsInAll)));

    /// <summary>
    /// Asks the per-client limiter first, and stops at the first refusal, so an attempt refused for
    /// its client never counts toward the limit in all, and a single host can't use that up either.
    /// </summary>
    private readonly PartitionedRateLimiter<IPAddress> _chain;

    public LoginThrottle()
    {
        _chain = PartitionedRateLimiter.CreateChained(_perClient, _inAll);
    }

    /// <summary>
    /// Counts an attempt from <paramref name="address"/>, and returns whether it's allowed.
    /// </summary>
    public bool TryAttempt(IPAddress? address)
    {
        using var lease = _chain.AttemptAcquire(Normalize(address));
        return lease.IsAcquired;
    }

    /// <summary>
    /// Disposes the chain and, since it doesn't dispose them itself, the limiters in it.
    /// </summary>
    public void Dispose()
    {
        _chain.Dispose();
        _perClient.Dispose();
        _inAll.Dispose();
    }

    private static FixedWindowRateLimiterOptions Limit(int attempts) => new()
    {
        PermitLimit = attempts,
        Window = Window,
        QueueLimit = 0,
    };

    /// <summary>
    /// Dual-mode sockets report IPv4 clients as IPv4-mapped IPv6 addresses; either way it's one
    /// client. No address (no TCP connection) is one shared client.
    /// </summary>
    private static IPAddress Normalize(IPAddress? address) => address switch
    {
        null => IPAddress.None,
        { IsIPv4MappedToIPv6: true } => address.MapToIPv4(),
        _ => address,
    };
}
