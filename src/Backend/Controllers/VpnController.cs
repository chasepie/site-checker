using Microsoft.AspNetCore.Mvc;
using SiteChecker.Domain.Ports;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class VpnController(
    ILogger<VpnController> logger,
    IVpnService vpnService)
    : ControllerBase
{
    private readonly IVpnService _vpnService = vpnService;
    private readonly ILogger<VpnController> _logger = logger;

    [HttpPost("ChangeLocation")]
    public async Task<VpnLocation> ChangeLocation(
        [FromQuery] bool excludeCurrent,
        CancellationToken cancellationToken)
    {
        return await _vpnService.ChangeLocationAsync(excludeCurrent, cancellationToken);
    }

    [HttpGet("CurrentLocation")]
    public async Task<VpnLocation> GetCurrentLocation(
        CancellationToken cancellationToken)
    {
        return await _vpnService.GetCurrentLocationAsync(cancellationToken);
    }

    [HttpGet("AllLocations")]
    public async Task<IReadOnlyList<VpnLocation>> GetAllLocations(
        CancellationToken cancellationToken)
    {
        return await _vpnService.GetAllLocationsAsync(cancellationToken);
    }
}
