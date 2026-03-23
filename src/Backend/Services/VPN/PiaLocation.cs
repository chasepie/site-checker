using System.Text.Json.Serialization;

namespace SiteChecker.Backend.Services.VPN;

public class PiaLocation
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("portForward")]
    public bool PortForward { get; set; }

    [JsonPropertyName("excluded")]
    public bool Excluded { get; set; } = false;

    public static PiaLocation NoVPN => new()
    {
        Name = "No VPN",
        Id = "no_vpn",
        PortForward = false,
        Excluded = false
    };
}
