using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using SiteChecker.Utilities;

namespace SiteChecker.Backend.Services.VPN;

/// <summary>
/// The Docker side of the PIA VPN: the VPN and Browserless VPN containers, and the file holding the
/// VPN container's location.
/// </summary>
public interface IPiaContainers
{
    /// <summary>
    /// Whether the Browserless VPN container is running.
    /// </summary>
    Task<bool> IsVpnRunningAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The VPN Locations to rotate through, in rotation order.
    /// </summary>
    Task<List<PiaLocation>> ListLocationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The ID of the location the VPN container is set to.
    /// </summary>
    Task<string> ReadCurrentLocationIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the VPN container's location and restarts it and the Browserless VPN container.
    /// </summary>
    Task SetLocationAndRestartAsync(string locationId, CancellationToken cancellationToken);
}

public sealed class DockerPiaContainers : IPiaContainers, IDisposable
{
    private const string PIA_CONTAINER_NAME = nameof(PIA_CONTAINER_NAME);
    private const string BROWSERLESS_VPN_CONTAINER_NAME = nameof(BROWSERLESS_VPN_CONTAINER_NAME);

    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();
    private readonly string _piaContainerName;
    private readonly string _brwsrContainerName;
    private readonly string _piaLocFilePath;

    public DockerPiaContainers(IConfiguration configuration)
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

    public async Task<bool> IsVpnRunningAsync(CancellationToken cancellationToken)
    {
        var ctnr = await GetContainerAsync(_brwsrContainerName, cancellationToken);
        return ctnr?.State == "running";
    }

    /// <summary>
    /// Lists PIA's locations with a throwaway container, keeping the US ones in a random order.
    /// </summary>
    public async Task<List<PiaLocation>> ListLocationsAsync(CancellationToken cancellationToken)
    {
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
        List<PiaLocation> locations;
        using (var logsStream = await _dockerClient.Containers.GetContainerLogsAsync(container.ID, false, logsParameters, cancellationToken))
        {
            (string stdout, string stderr) = await logsStream.ReadOutputToEndAsync(cancellationToken);

            var jsonStart = stdout.IndexOf('{');
            var innerJson = stdout[jsonStart..].Trim()
                .Replace("}", "},")
                .Replace("\"port_forward\"", "\"portForward\"")
                .TrimEnd(',');
            var json = '[' + innerJson + ']';
            var allLocations = JsonSerializer.Deserialize<List<PiaLocation>>(json)
                ?? throw new JsonException($"Failed to deserialize locations: {stdout}");
            locations = allLocations
                .Where(l => l.Id.StartsWith("us_", StringComparison.Ordinal)
                    || l.Id.StartsWith("us-", StringComparison.Ordinal))
                .Shuffle()
                .ToList();
        }
        await _dockerClient.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
        return locations;
    }

    public async Task<string> ReadCurrentLocationIdAsync(CancellationToken cancellationToken)
    {
        var currentLoc = await File.ReadAllTextAsync(_piaLocFilePath, cancellationToken);
        return currentLoc.Trim();
    }

    public async Task SetLocationAndRestartAsync(string locationId, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(_piaLocFilePath, locationId, cancellationToken);

        var vpnContainer = await GetContainerAsync(_piaContainerName, cancellationToken);
        await _dockerClient.Containers.RestartContainerAsync(vpnContainer!.ID, new(), cancellationToken);

        var browserlessContainer = await GetContainerAsync(_brwsrContainerName, cancellationToken);
        await _dockerClient.Containers.RestartContainerAsync(browserlessContainer!.ID, new(), cancellationToken);
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

    public void Dispose() => _dockerClient.Dispose();
}
