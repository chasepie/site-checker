namespace SiteChecker.Backend.Services.VPN;

/// <summary>
/// Tracks the VPN Locations, which of them are excluded, and rotation between them. Exclusions are
/// global (every VPN Site skips an excluded location) and held in memory, so a restart clears them.
/// Call it through the Site Check Runner when changing location, so a change never overlaps a scrape.
/// </summary>
public sealed class PiaService(IPiaContainers containers)
{
    /// <summary>
    /// Below this many eligible locations, every exclusion is reset.
    /// </summary>
    private const int MinimumEligibleLocations = 5;

    private readonly IPiaContainers _containers = containers;
    private List<PiaLocation>? _locations;

    /// <summary>
    /// Moves to the next eligible location after the current one, in list order, and restarts the
    /// VPN containers there.
    /// </summary>
    /// <param name="excludeCurrentLocation">Whether to exclude the current location as well.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The new location, or <see cref="PiaLocation.NoVPN"/> if the VPN isn't running.</returns>
    public async Task<PiaLocation> ChangeLocationAsync(
        bool excludeCurrentLocation,
        CancellationToken cancellationToken = default)
    {
        if (!await _containers.IsVpnRunningAsync(cancellationToken))
        {
            return PiaLocation.NoVPN;
        }

        var locations = await GetLocationsAsync(cancellationToken);
        if (locations.Count(l => !l.Excluded) < MinimumEligibleLocations)
        {
            locations.ForEach(l => l.Excluded = false);
        }

        var currentLoc = await GetCurrentLocationAsync(cancellationToken);
        if (excludeCurrentLocation)
        {
            currentLoc.Excluded = true;
        }

        var nextLocation = NextEligibleAfter(locations, currentLoc.Id);
        await _containers.SetLocationAndRestartAsync(nextLocation.Id, cancellationToken);
        return nextLocation;
    }

    /// <summary>
    /// Excludes a location from rotation until exclusions are reset.
    /// </summary>
    public void ExcludeLocation(string locationId)
    {
        var location = _locations?.FirstOrDefault(l => l.Id == locationId);
        if (location != null)
        {
            location.Excluded = true;
        }
    }

    public async Task<PiaLocation> GetCurrentLocationAsync(CancellationToken cancellationToken)
    {
        if (!await _containers.IsVpnRunningAsync(cancellationToken))
        {
            return PiaLocation.NoVPN;
        }

        var currentLocation = await _containers.ReadCurrentLocationIdAsync(cancellationToken);
        var locations = await GetLocationsAsync(cancellationToken);
        return locations.FirstOrDefault(l => l.Id == currentLocation)
            ?? throw new KeyNotFoundException($"Location '{currentLocation}' not found in locations list");
    }

    public async Task<List<PiaLocation>> GetAllLocationsAsync(CancellationToken cancellationToken)
    {
        if (!await _containers.IsVpnRunningAsync(cancellationToken))
        {
            return [PiaLocation.NoVPN];
        }

        var locations = await GetLocationsAsync(cancellationToken);
        return locations.OrderBy(l => l.Id).ToList();
    }

    /// <summary>
    /// The first location after <paramref name="currentId"/> in the full list order, wrapping
    /// around, that isn't excluded. Searching the full list, rather than the eligible ones, keeps the
    /// rotation moving forward when the current location is itself excluded.
    /// </summary>
    private static PiaLocation NextEligibleAfter(List<PiaLocation> locations, string currentId)
    {
        if (locations.Count == 0)
        {
            throw new InvalidOperationException("PIA listed no VPN Locations to rotate through.");
        }

        var currentIndex = locations.FindIndex(l => l.Id == currentId);
        for (var offset = 1; offset <= locations.Count; offset++)
        {
            var candidate = locations[(currentIndex + offset) % locations.Count];
            if (!candidate.Excluded && candidate.Id != currentId)
            {
                return candidate;
            }
        }

        // No other location is eligible; stay where we are.
        return locations[Math.Max(currentIndex, 0)];
    }

    private async Task<List<PiaLocation>> GetLocationsAsync(CancellationToken cancellationToken)
    {
        return _locations ??= await _containers.ListLocationsAsync(cancellationToken);
    }
}

public static class PiaServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPiaService()
        {
            return services
                .AddSingleton<IPiaContainers, DockerPiaContainers>()
                .AddSingleton<PiaService>();
        }
    }
}
