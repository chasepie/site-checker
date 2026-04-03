namespace SiteChecker.Domain.ValueObjects;

public record VpnLocation(string Id, string Name)
{
    public static VpnLocation NoVpn { get; } = new("no_vpn", "No VPN");
}
