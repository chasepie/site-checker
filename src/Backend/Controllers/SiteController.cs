using Microsoft.AspNetCore.Mvc;
using SiteChecker.Application.UseCases;
using SiteChecker.Backend.Extensions;
using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;

namespace SiteChecker.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SiteController(ManageSitesUseCase manageSites)
    : ControllerBase
{
    private readonly ManageSitesUseCase _manageSites = manageSites;

    private CancellationToken CancellationToken => HttpContext.RequestAborted;

    [HttpGet]
    public async Task<ActionResult<List<Site>>> GetAllSites()
    {
        var sites = await _manageSites.GetAllSitesAsync(CancellationToken);
        return Ok(sites);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Site>> GetSite([FromRoute] int id)
    {
        var site = await _manageSites.GetSiteAsync(id, CancellationToken);
        return this.OkOrNotFound(site);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Site>> UpdateSite(
        [FromRoute] int id,
        [FromBody] SiteUpdate siteUpdate)
    {
        if (id != siteUpdate.Id)
        {
            return BadRequest();
        }

        var site = await _manageSites.UpdateSiteAsync(siteUpdate, CancellationToken);
        return this.OkOrNotFound(site);
    }
}
