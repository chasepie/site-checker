using Microsoft.AspNetCore.Mvc;
using SiteChecker.Backend.Services.VPN;

namespace SiteChecker.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class VpnController(
    ILogger<VpnController> logger,
    PiaService piaService)
    : ControllerBase
{
    private readonly PiaService _piaService = piaService;
    private readonly ILogger<VpnController> _logger = logger;

    [HttpPost("ChangeLocation")]
    public async Task<PiaLocation> ChangeLocation(
        [FromQuery] bool excludeCurrent,
        CancellationToken cancellationToken)
    {
        return await _piaService.ChangeLocationAsync(excludeCurrent, cancellationToken);
    }

    [HttpGet("CurrentLocation")]
    public async Task<PiaLocation> GetCurrentLocation(
        CancellationToken cancellationToken)
    {
        return await _piaService.GetCurrentLocationAsync(cancellationToken);
    }

    [HttpGet("AllLocations")]
    public async Task<List<PiaLocation>> GetAllLocations(
        CancellationToken cancellationToken)
    {
        return await _piaService.GetAllLocationsAsync(cancellationToken);
    }
}
