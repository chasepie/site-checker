namespace SiteChecker.Backend.UnitTests;

using System.Net;
using System.Text.Json;
using SiteChecker.Backend.Services.VPN;

[TestClass]
public sealed class PiaServerListTests
{
    /// <summary>
    /// The shape of PIA's real response, cut down: one line of JSON, then the base64 signature.
    /// Ohio has no WireGuard servers, and the Netherlands isn't in the US.
    /// </summary>
    private const string ServerList = """
        {"groups":{"wg":[{"name":"wireguard","ports":[1337]}]},"regions":[{"id":"us_california","name":"US California","country":"US","auto_region":true,"dns":"us-california.privacy.network","port_forward":false,"geo":false,"offline":false,"servers":{"meta":[{"ip":"1.2.3.4","cn":"losangeles401"}],"wg":[{"ip":"1.2.3.5","cn":"losangeles401"}]}},{"id":"us-newjersey","name":"US East","country":"US","auto_region":true,"dns":"us-newjersey.privacy.network","port_forward":false,"geo":false,"offline":false,"servers":{"wg":[{"ip":"1.2.3.6","cn":"newjersey402"}]}},{"id":"us_alabama-pf","name":"US Alabama","country":"US","auto_region":true,"dns":"us-alabama-pf.privacy.network","port_forward":true,"geo":true,"offline":false,"servers":{"wg":[{"ip":"1.2.3.7","cn":"alabama403"}]}},{"id":"us_ohio-pf","name":"US Ohio","country":"US","auto_region":true,"dns":"us-ohio-pf.privacy.network","port_forward":true,"geo":true,"offline":false,"servers":{"ovpnudp":[{"ip":"1.2.3.8","cn":"ohio404","van":true}],"wg":[]}},{"id":"nl_amsterdam","name":"NL Netherlands","country":"NL","auto_region":true,"dns":"nl-amsterdam.privacy.network","port_forward":true,"geo":false,"offline":false,"servers":{"wg":[{"ip":"1.2.3.9","cn":"amsterdam405"}]}}]}
        3dgsuYATEoeZC6YIvLeipoikDVoHk69lhEVKcksXl2yCBWbbPI54aYwv5x6JfTIvrE13+//3I
        oBWXy5l5rpBsV5GF9ueh585NjGXnUFMcir/QsNlenQ==
        """;

    private static readonly string[] WireGuardIds = ["us_california", "us-newjersey", "us_alabama-pf", "nl_amsterdam"];
    private static readonly string[] UsWireGuardIds = ["us_california", "us-newjersey", "us_alabama-pf"];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private sealed class FakeServerListApi(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri?> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [TestMethod]
    public void Parse_ReadsEveryRegionWithWireGuard()
    {
        var locations = PiaServerList.Parse(ServerList);

        CollectionAssert.AreEqual(WireGuardIds, locations.Select(l => l.Id).ToArray());
        var alabama = locations.Single(l => l.Id == "us_alabama-pf");
        Assert.AreEqual("US Alabama", alabama.Name);
        Assert.IsTrue(alabama.PortForward);
        Assert.IsFalse(alabama.Excluded);
        Assert.IsFalse(locations.Single(l => l.Id == "us_california").PortForward);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("""{"groups":{}}""")]
    public void Parse_RejectsAListWithoutRegions(string body)
    {
        Assert.Throws<JsonException>(() => PiaServerList.Parse(body));
    }

    [TestMethod]
    [DataRow("""{"regions":[{"name":"US East","servers":{"wg":[{"ip":"1.2.3.6"}]}}]}""")]
    [DataRow("""{"regions":[{"id":"us-newjersey","name":7,"servers":{"wg":[{"ip":"1.2.3.6"}]}}]}""")]
    public void Parse_RejectsARegionWithoutAnIdOrName(string body)
    {
        Assert.Throws<JsonException>(() => PiaServerList.Parse(body));
    }

    [TestMethod]
    public async Task ListLocations_KeepsOnlyTheUsLocations()
    {
        var api = new FakeServerListApi(HttpStatusCode.OK, ServerList);
        using var httpClient = new HttpClient(api);

        var locations = await new PiaServerList(httpClient).ListLocationsAsync(Ct);

        CollectionAssert.AreEquivalent(UsWireGuardIds, locations.Select(l => l.Id).ToArray());
        Assert.AreEqual(PiaServerList.ServerListUri, Assert.ContainsSingle(api.Requests));
    }

    [TestMethod]
    public async Task ListLocations_FailsOnAnErrorResponse()
    {
        using var httpClient = new HttpClient(new FakeServerListApi(HttpStatusCode.ServiceUnavailable, "down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => new PiaServerList(httpClient).ListLocationsAsync(Ct));
    }
}
