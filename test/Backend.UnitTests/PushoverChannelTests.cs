namespace SiteChecker.Backend.UnitTests;

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiteChecker.Backend.Notifiers;
using SiteChecker.Backend.Notifiers.Pushover;
using SiteChecker.Database.Model;

[TestClass]
public sealed class PushoverChannelTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    /// <summary>
    /// Captures the form fields of each request Pushover would receive.
    /// </summary>
    private sealed class FakePushoverApi(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Dictionary<string, string>> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>();
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                fields[name] = await part.ReadAsStringAsync(cancellationToken);
            }
            Requests.Add(fields);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }

    private static PushoverChannel CreateChannel(FakePushoverApi api)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PushoverChannel.PushoverUserKey] = "user",
                [PushoverChannel.PushoverTokenKey] = "token",
            })
            .Build();
        var httpClient = new HttpClient(api) { BaseAddress = new Uri("https://pushover.test") };
        return new PushoverChannel(httpClient, config, NullLogger<PushoverChannel>.Instance);
    }

    private static Site SiteWith(PushoverPriority? success, PushoverPriority? failure) => new()
    {
        Name = "Site",
        Url = new Uri("https://example.com"),
        ScraperId = "SCRAPER",
        PushoverConfig = new PushoverConfig { SuccessPriority = success, FailurePriority = failure },
    };

    private static Notification Notification(NotificationKind kind, NotificationSettings settings)
        => new(kind, settings, "Title", "Body", new Uri("https://example.com"), Screenshot: null);

    [TestMethod]
    public async Task EmergencyAlert_IncludesRetryAndExpire()
    {
        using var api = new FakePushoverApi();
        var channel = CreateChannel(api);

        var sent = await channel.SendAsync(
            Notification(NotificationKind.Failing, NotificationSettings.Failure),
            SiteWith(null, PushoverPriority.Emergency),
            Ct);

        Assert.IsTrue(sent);
        var fields = Assert.ContainsSingle(api.Requests);
        Assert.AreEqual("2", fields["priority"]);
        Assert.AreEqual("60", fields["retry"]);
        Assert.AreEqual("3600", fields["expire"]);
    }

    [TestMethod]
    public async Task EmergencyRecovery_IsLoweredToHigh()
    {
        using var api = new FakePushoverApi();
        var channel = CreateChannel(api);

        await channel.SendAsync(
            Notification(NotificationKind.Recovered, NotificationSettings.Failure),
            SiteWith(null, PushoverPriority.Emergency),
            Ct);

        var fields = Assert.ContainsSingle(api.Requests);
        Assert.AreEqual("1", fields["priority"]);
        Assert.IsFalse(fields.ContainsKey("retry"));
        Assert.IsFalse(fields.ContainsKey("expire"));
    }

    [TestMethod]
    public async Task ReturnsFalse_AndSendsNothing_WhenOffForTheNotificationsSettings()
    {
        using var api = new FakePushoverApi();
        var channel = CreateChannel(api);

        var sent = await channel.SendAsync(
            Notification(NotificationKind.Updated, NotificationSettings.Success),
            SiteWith(null, PushoverPriority.High),
            Ct);

        Assert.IsFalse(sent);
        Assert.IsEmpty(api.Requests);
    }

    [TestMethod]
    public async Task FallsBackToSuccessPriority_ForFailureThenSuccess()
    {
        using var api = new FakePushoverApi();
        var channel = CreateChannel(api);

        await channel.SendAsync(
            Notification(NotificationKind.RecoveredAndUpdated, NotificationSettings.FailureThenSuccess),
            SiteWith(PushoverPriority.Low, null),
            Ct);

        Assert.AreEqual("-1", Assert.ContainsSingle(api.Requests)["priority"]);
    }

    [TestMethod]
    public async Task Throws_WhenPushoverRejectsTheMessage()
    {
        using var api = new FakePushoverApi(HttpStatusCode.BadRequest);
        var channel = CreateChannel(api);

        await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(
            Notification(NotificationKind.Failing, NotificationSettings.Failure),
            SiteWith(null, PushoverPriority.Normal),
            Ct));
    }
}
