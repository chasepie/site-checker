using Docker.DotNet;
using SiteChecker.Utilities;

namespace SiteChecker.Backend.Services.VPN;

/// <summary>
/// The outside of the PIA VPN: the VPN and Browserless VPN containers, the file holding the VPN
/// container's location, and PIA's list of locations.
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

/// <summary>
/// Controls the VPN containers through the Docker API, addressing them only by name, so the app
/// needs nothing but "inspect the Browserless VPN container" and "restart the two VPN containers".
/// <c>DOCKER_HOST</c> points it at a socket proxy that allows just that (see
/// <c>docker-compose.yml</c>); without it, the local Docker socket is used.
/// </summary>
public sealed class DockerPiaContainers : IPiaContainers, IDisposable
{
    public const string DockerHostKey = "DOCKER_HOST";
    private const string PIA_CONTAINER_NAME = nameof(PIA_CONTAINER_NAME);
    private const string BROWSERLESS_VPN_CONTAINER_NAME = nameof(BROWSERLESS_VPN_CONTAINER_NAME);

    private readonly DockerClient _dockerClient;
    private readonly PiaServerList _serverList;
    private readonly string _piaContainerName;
    private readonly string _brwsrContainerName;
    private readonly string _piaLocFilePath;

    public DockerPiaContainers(IConfiguration configuration, PiaServerList serverList)
    {
        _serverList = serverList;

        var dockerHost = configuration[DockerHostKey];
        _dockerClient = (string.IsNullOrWhiteSpace(dockerHost)
                ? new DockerClientConfiguration()
                : new DockerClientConfiguration(new Uri(dockerHost)))
            .CreateClient();

        // Names, not paths: the API takes "site-checker-vpn" where container lists show "/site-checker-vpn".
        _piaContainerName = (configuration[PIA_CONTAINER_NAME] ?? "site-checker-vpn").TrimStart('/');
        _brwsrContainerName = (configuration[BROWSERLESS_VPN_CONTAINER_NAME] ?? "site-checker-browserless-vpn").TrimStart('/');

        _piaLocFilePath = Path.Join(AppDirectories.Pia, "loc.txt");
    }

    public async Task<bool> IsVpnRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var container = await _dockerClient.Containers.InspectContainerAsync(_brwsrContainerName, cancellationToken);
            return container.State?.Running == true;
        }
        catch (DockerContainerNotFoundException)
        {
            return false;
        }
    }

    public Task<List<PiaLocation>> ListLocationsAsync(CancellationToken cancellationToken)
        => _serverList.ListLocationsAsync(cancellationToken);

    public async Task<string> ReadCurrentLocationIdAsync(CancellationToken cancellationToken)
    {
        var currentLoc = await File.ReadAllTextAsync(_piaLocFilePath, cancellationToken);
        return currentLoc.Trim();
    }

    public async Task SetLocationAndRestartAsync(string locationId, CancellationToken cancellationToken)
    {
        // Replaced rather than rewritten: the VPN container creates loc.txt as root on its first
        // start, and the app's non-root user can replace a file in a directory it owns but can't
        // write to one it doesn't.
        var tempPath = _piaLocFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, locationId, cancellationToken);
        File.Move(tempPath, _piaLocFilePath, overwrite: true);
        await _dockerClient.Containers.RestartContainerAsync(_piaContainerName, new(), cancellationToken);
        await _dockerClient.Containers.RestartContainerAsync(_brwsrContainerName, new(), cancellationToken);
    }

    public void Dispose() => _dockerClient.Dispose();
}
