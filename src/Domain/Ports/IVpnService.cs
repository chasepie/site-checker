using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Domain.Ports;

public interface IVpnService
{
    Task<VpnLocation> GetCurrentLocationAsync(CancellationToken cancellationToken = default);

    Task<VpnLocation> ChangeLocationAsync(
        bool excludeCurrentLocation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VpnLocation>> GetAllLocationsAsync(CancellationToken cancellationToken = default);
}
