using Microsoft.AspNetCore.Mvc;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Backend.Services.VPN;

namespace SiteChecker.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class VpnController(
    PiaService piaService,
    SiteCheckRunner runner)
    : ControllerBase
{
    private readonly PiaService _piaService = piaService;
    private readonly SiteCheckRunner _runner = runner;

    private CancellationToken CancellationToken => HttpContext.RequestAborted;

    /// <summary>
    /// Changes the VPN Location, after any running check finishes.
    /// </summary>
    [HttpPost("ChangeLocation")]
    public async Task<PiaLocation> ChangeLocation(
        [FromQuery] bool excludeCurrent)
    {
        return await _runner.ChangeVpnLocationAsync(excludeCurrent, CancellationToken);
    }

    [HttpGet("CurrentLocation")]
    public async Task<PiaLocation> GetCurrentLocation()
    {
        return await _piaService.GetCurrentLocationAsync(CancellationToken);
    }

    [HttpGet("AllLocations")]
    public async Task<List<PiaLocation>> GetAllLocations()
    {
        return await _piaService.GetAllLocationsAsync(CancellationToken);
    }
}
