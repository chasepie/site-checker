using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Database;
using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;

namespace SiteChecker.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SiteController(SiteCheckerDbContext dbContext)
    : ControllerBase
{
    private readonly SiteCheckerDbContext _dbContext = dbContext;

    private CancellationToken CancellationToken => HttpContext.RequestAborted;

    [HttpGet]
    public async Task<ActionResult<List<Site>>> GetAllSites()
    {
        var sites = await _dbContext.Sites
            .AsNoTracking()
            .ToListAsync(CancellationToken);
        return Ok(sites);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Site>> GetSite([FromRoute] int id)
    {
        var site = await _dbContext.Sites
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, CancellationToken);
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

        var site = await _dbContext.Sites
            .FirstOrDefaultAsync(s => s.Id == id, CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        site.Update(siteUpdate);
        await _dbContext.SaveChangesAsync(CancellationToken);
        return Ok(site);
    }
}
