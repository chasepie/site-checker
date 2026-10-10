namespace SiteChecker.Backend.UnitTests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using SiteChecker.Backend.Services.SignalR;

[TestClass]
public sealed class HubConnectionsTests
{
    private sealed class FakeContext(string connectionId) : HubCallerContext
    {
        public bool Aborted { get; private set; }

        public override string ConnectionId => connectionId;

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() => Aborted = true;
    }

    [TestMethod]
    public void CloseAll_AbortsEveryOpenConnection_ButNotClosedOnes()
    {
        var connections = new HubConnections();
        var open = new FakeContext("open");
        var another = new FakeContext("another");
        var closed = new FakeContext("closed");
        connections.Add(open);
        connections.Add(another);
        connections.Add(closed);
        connections.Remove(closed);

        connections.CloseAll();

        Assert.IsTrue(open.Aborted);
        Assert.IsTrue(another.Aborted);
        Assert.IsFalse(closed.Aborted);
    }
}
