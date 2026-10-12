namespace SiteChecker.Backend.Test;

using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using SiteChecker.Backend.Services.SignalR;

/// <summary>
/// A hub connection that records whether it was aborted, opened by the login
/// <paramref name="sessionId"/>, or without a login when it's <c>null</c>.
/// </summary>
internal sealed class FakeHubCallerContext(string? sessionId = null) : HubCallerContext
{
    public bool Aborted { get; private set; }

    public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");

    public override string? UserIdentifier => null;

    public override ClaimsPrincipal? User { get; } = sessionId is null
        ? null
        : new ClaimsPrincipal(new ClaimsIdentity([new Claim(HubConnections.SessionIdClaim, sessionId)], "Test"));

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features { get; } = new FeatureCollection();

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort() => Aborted = true;
}

[TestClass]
public sealed class HubConnectionsTests
{
    [TestMethod]
    public void CloseSession_AbortsThatSessionsOpenConnections_ButNotOtherSessionsOrClosedOnes()
    {
        var connections = new HubConnections();
        var tab = new FakeHubCallerContext("session");
        var anotherTab = new FakeHubCallerContext("session");
        var closed = new FakeHubCallerContext("session");
        var otherSession = new FakeHubCallerContext("other-session");
        var noLogin = new FakeHubCallerContext();
        connections.Add(tab);
        connections.Add(anotherTab);
        connections.Add(closed);
        connections.Add(otherSession);
        connections.Add(noLogin);
        connections.Remove(closed);

        connections.CloseSession("session");

        Assert.IsTrue(tab.Aborted);
        Assert.IsTrue(anotherTab.Aborted);
        Assert.IsFalse(closed.Aborted);
        Assert.IsFalse(otherSession.Aborted);
        Assert.IsFalse(noLogin.Aborted);
    }
}
