using System.Text.Json;

namespace SiteChecker.Backend.Services.VPN;

/// <summary>
/// PIA's public server list, the same one the VPN container's <c>wg-gen.sh -a</c> reads. It needs
/// no credentials. The response is the list as one line of JSON, followed by its signature, which
/// isn't verified.
/// </summary>
public sealed class PiaServerList(HttpClient httpClient)
{
    public static readonly Uri ServerListUri = new("https://serverlist.piaservers.net/vpninfo/servers/v6");

    private readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// The US locations that offer WireGuard, in a random order.
    /// </summary>
    public async Task<List<PiaLocation>> ListLocationsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(ServerListUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return Parse(body)
            .Where(l => l.Id.StartsWith("us_", StringComparison.Ordinal)
                || l.Id.StartsWith("us-", StringComparison.Ordinal))
            .Shuffle()
            .ToList();
    }

    /// <summary>
    /// Every region in the list that has WireGuard servers, in list order.
    /// </summary>
    /// <exception cref="JsonException">The list isn't in the expected format.</exception>
    public static List<PiaLocation> Parse(string body)
    {
        var newline = body.IndexOf('\n', StringComparison.Ordinal);
        var json = newline < 0 ? body : body[..newline];

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("regions", out var regions)
            || regions.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The server list has no regions.");
        }

        var locations = new List<PiaLocation>();
        foreach (var region in regions.EnumerateArray())
        {
            if (!region.TryGetProperty("servers", out var servers)
                || !servers.TryGetProperty("wg", out var wireGuard)
                || wireGuard.ValueKind != JsonValueKind.Array
                || wireGuard.GetArrayLength() == 0)
            {
                continue;
            }

            locations.Add(new PiaLocation
            {
                Id = region.GetProperty("id").GetString()
                    ?? throw new JsonException("A region has no id."),
                Name = region.GetProperty("name").GetString()
                    ?? throw new JsonException("A region has no name."),
                PortForward = region.TryGetProperty("port_forward", out var portForward)
                    && portForward.ValueKind == JsonValueKind.True,
            });
        }
        return locations;
    }
}
