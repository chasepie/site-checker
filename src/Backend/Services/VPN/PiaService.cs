using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using SiteChecker.Domain.Ports;
using SiteChecker.Domain.ValueObjects;
using SiteChecker.Utilities;

namespace SiteChecker.Backend.Services.VPN;

public class PiaService : IVpnService, IDisposable
{
    private const string PIA_CONTAINER_NAME = nameof(PIA_CONTAINER_NAME);
    private const string BROWSERLESS_VPN_CONTAINER_NAME = nameof(BROWSERLESS_VPN_CONTAINER_NAME);

    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();
    private readonly string _piaContainerName;
    private readonly string _brwsrContainerName;
    private readonly string _piaLocFilePath;

    private bool disposedValue;
    private List<PiaLocation>? _locations;

    public PiaService(IConfiguration configuration)
    {
        _piaContainerName = configuration[PIA_CONTAINER_NAME] ?? "/site-checker-vpn";
        _brwsrContainerName = configuration[BROWSERLESS_VPN_CONTAINER_NAME] ?? "/site-checker-browserless-vpn";

        string piaDir;
        if (!EnvironmentUtils.IsDockerContainer() && RepoUtils.TryGetRepoDirectory(out var repoDir))
        {
            piaDir = Path.Join(repoDir, "site-checker/pia");
        }
        else
        {
            piaDir = "/pia";
        }
        _piaLocFilePath = Path.Join(piaDir, "loc.txt");
    }

    public async Task<PiaLocation> ChangeLocationAsync(
        bool excludeCurrentLocation,
        CancellationToken cancellationToken = default)
    {
        if (!await IsVpnContainerRunningAsync(cancellationToken))
        {
            return PiaLocation.NoVPN;
        }

        var locations = await GetLocationsAsync(cancellationToken);
        var eligibleLocations = locations
            .Where(l => !l.Excluded)
            .ToList();

        // Reset exclusions if too few locations remain
        if (eligibleLocations.Count < 5)
        {
            locations.ForEach(l => l.Excluded = false);
            eligibleLocations = locations.ToList();
        }

        var currentLoc = await GetCurrentLocationAsync(cancellationToken);
        if (excludeCurrentLocation)
        {
            currentLoc.Excluded = true;
            eligibleLocations.RemoveAll(l => l.Id == currentLoc.Id);
        }

        // Get next location in list
        var currentPos = eligibleLocations.FindIndex(l => l.Id == currentLoc.Id);
        var nextLocation = currentPos == eligibleLocations.Count - 1
            ? eligibleLocations[0]
            : eligibleLocations[currentPos + 1];

        // Restart VPN container with new location
        await File.WriteAllTextAsync(_piaLocFilePath, nextLocation.Id, cancellationToken);
        await RestartContainersAsync(cancellationToken);

        return nextLocation;
    }

    public async Task<PiaLocation> GetCurrentLocationAsync(CancellationToken cancellationToken)
    {
        if (!await IsVpnContainerRunningAsync(cancellationToken))
        {
            return PiaLocation.NoVPN;
        }

        var currentLocation = await ReadLocationFileAsync(cancellationToken);
        var locations = await GetLocationsAsync(cancellationToken);
        return locations.FirstOrDefault(l => l.Id == currentLocation)
            ?? throw new KeyNotFoundException($"Location '{currentLocation}' not found in locations list");
    }

    public async Task<List<PiaLocation>> GetAllLocationsAsync(CancellationToken cancellationToken)
    {
        if (!await IsVpnContainerRunningAsync(cancellationToken))
        {
            return [PiaLocation.NoVPN];
        }

        var locations = await GetLocationsAsync(cancellationToken);
        return locations.OrderBy(l => l.Id).ToList();
    }

    private async Task<List<PiaLocation>> GetLocationsAsync(CancellationToken cancellationToken)
    {
        if (_locations is not null)
        {
            return _locations;
        }

        var createOptions = new CreateContainerParameters()
        {
            Image = "thrnz/docker-wireguard-pia",
            Cmd = ["/scripts/wg-gen.sh", "-a"],
        };

        var container = await _dockerClient.Containers.CreateContainerAsync(createOptions, cancellationToken);
        await _dockerClient.Containers.StartContainerAsync(container.ID, new(), cancellationToken);
        var waitResponse = await _dockerClient.Containers.WaitContainerAsync(container.ID, cancellationToken);
        if (waitResponse.StatusCode != 0)
        {
            throw new InvalidOperationException($"Container exited with code {waitResponse.StatusCode}");
        }

        var logsParameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = false,
            Follow = false,
        };
        using (var logsStream = await _dockerClient.Containers.GetContainerLogsAsync(container.ID, false, logsParameters, cancellationToken))
        {
            (string stdout, string stderr) = await logsStream.ReadOutputToEndAsync(cancellationToken);

            var jsonStart = stdout.IndexOf('{');
            var innerJson = stdout[jsonStart..].Trim()
                .Replace("}", "},")
                .Replace("\"port_forward\"", "\"portForward\"")
                .TrimEnd(',');
            var json = '[' + innerJson + ']';
            var locations = JsonSerializer.Deserialize<List<PiaLocation>>(json)
                ?? throw new JsonException($"Failed to deserialize locations: {stdout}");
            _locations = locations
                .Where(l => l.Id.StartsWith("us_", StringComparison.Ordinal)
                    || l.Id.StartsWith("us-", StringComparison.Ordinal))
                .Shuffle()
                .ToList();
        }
        await _dockerClient.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
        return _locations;
    }

    private async Task<ContainerListResponse?> GetContainerAsync(string name, CancellationToken ct)
    {
        var config = new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                { "name", new Dictionary<string, bool> { { name, true } } }
            }
        };
        var results = await _dockerClient.Containers.ListContainersAsync(config, ct);

        var containers = results
            .Where(c => c.Names.Contains(name, StringComparer.InvariantCultureIgnoreCase))
            .ToList();
        if (containers.Count > 1)
        {
            var names = containers.SelectMany(c => c.Names).ToList();
            throw new InvalidOperationException($"Found multiple containers with name '{name}': '{string.Join(", ", names)}'");
        }

        return containers.FirstOrDefault();
    }

    private async Task<string> ReadLocationFileAsync(
        CancellationToken cancellationToken)
    {
        await IsVpnContainerRunningAsync(cancellationToken);
        var currentLoc = await File.ReadAllTextAsync(_piaLocFilePath, cancellationToken);
        return currentLoc.Trim();
    }

    private async Task RestartContainersAsync(CancellationToken ct)
    {
        var vpnContainer = await GetContainerAsync(_piaContainerName, ct);
        await _dockerClient.Containers.RestartContainerAsync(vpnContainer!.ID, new(), ct);

        var browserlessContainer = await GetContainerAsync(_brwsrContainerName, ct);
        await _dockerClient.Containers.RestartContainerAsync(browserlessContainer!.ID, new(), ct);
    }

    private async Task<bool> IsVpnContainerRunningAsync(CancellationToken cancellationToken)
    {
        var ctnr = await GetContainerAsync(_brwsrContainerName, cancellationToken);
        return ctnr?.State == "running";
    }

    // ── IVpnService (domain port) ────────────────────────────────────────────

    async Task<VpnLocation> IVpnService.GetCurrentLocationAsync(CancellationToken cancellationToken)
    {
        var loc = await GetCurrentLocationAsync(cancellationToken);
        return new VpnLocation(loc.Id, loc.Name);
    }

    async Task<VpnLocation> IVpnService.ChangeLocationAsync(
        bool excludeCurrentLocation,
        CancellationToken cancellationToken)
    {
        var loc = await ChangeLocationAsync(excludeCurrentLocation, cancellationToken);
        return new VpnLocation(loc.Id, loc.Name);
    }

    async Task<IReadOnlyList<VpnLocation>> IVpnService.GetAllLocationsAsync(CancellationToken cancellationToken)
    {
        var locs = await GetAllLocationsAsync(cancellationToken);
        return locs.Select(l => new VpnLocation(l.Id, l.Name)).ToList();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _dockerClient.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}


public static class PiaServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPiaService()
        {
            services.AddSingleton<PiaService>();
            services.AddSingleton<IVpnService>(sp => sp.GetRequiredService<PiaService>());
            return services;
        }
    }
}
