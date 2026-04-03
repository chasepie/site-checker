using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Database;
using SiteChecker.Database.Extensions;
using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain;

namespace SiteChecker.Backend.Controllers;

[Route("api/Site/{siteId}/check")]
[ApiController]
public class SiteCheckController(SiteCheckerDbContext dbContext) : ControllerBase
{
    private CancellationToken CancellationToken => HttpContext.RequestAborted;
    private readonly SiteCheckerDbContext _dbContext = dbContext;

    [HttpGet]
    public async Task<ActionResult<PagedResponse<SiteCheck>>> GetAllSiteChecks(
        [FromRoute] int siteId,
        [FromQuery] int pageNumber = 0,
        [FromQuery] int pageSize = 10)
    {
        var siteChecks = await _dbContext.SiteChecks
            .AsNoTracking()
            .Where(sc => sc.SiteId == siteId)
            .OrderByDescending(sc => sc.StartDate)
            .ToPagedResponseAsync(pageNumber, pageSize, CancellationToken);
        return Ok(siteChecks);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SiteCheck>> GetSiteCheck(
        [FromRoute] int siteId,
        [FromRoute] int id)
    {
        var siteCheck = await _dbContext.SiteChecks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                sc => sc.Id == id && sc.SiteId == siteId,
                CancellationToken);

        return this.OkOrNotFound(siteCheck);
    }

    [HttpGet("{id}/screenshot")]
    public async Task<ActionResult<SiteCheckScreenshot>> GetScreenshot(
        [FromRoute] int siteId,
        [FromRoute] int id)
    {
        var exists = await _dbContext.SiteChecks
            .AsNoTracking()
            .AnyAsync(sc => sc.Id == id && sc.SiteId == siteId, CancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        var screenshot = await _dbContext.SiteCheckScreenshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SiteCheckId == id, CancellationToken);
        return this.OkOrNotFound(screenshot);
    }

    [HttpPost]
    public async Task<ActionResult<SiteCheck>> CreateSiteCheck(
        [FromRoute] int siteId)
    {
        var site = await _dbContext.Sites
            .FirstOrDefaultAsync(
                s => s.Id == siteId,
                CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        var siteCheck = new SiteCheck(site.Id);
        var entityEntry = _dbContext.SiteChecks.Add(siteCheck);
        await _dbContext.SaveChangesAsync(CancellationToken);

        return CreatedAtAction(
            nameof(GetSiteCheck),
            new { siteId, id = siteCheck.Id },
            entityEntry.Entity);
    }

    [HttpPost(nameof(CreateEmptyCheck))]
    public async Task<ActionResult<SiteCheck>> CreateEmptyCheck(
        [FromRoute] int siteId)
    {
        var site = await _dbContext.Sites
            .FirstOrDefaultAsync(
                s => s.Id == siteId,
                CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        var siteCheck = new SiteCheck(site.Id);
        var empty = new SuccessScrapeResult
        {
            Content = "[Empty Check]"
        };
        siteCheck.Update(empty);
        _dbContext.SiteChecks.Add(siteCheck);
        await _dbContext.SaveChangesAsync(CancellationToken);

        return CreatedAtAction(
            nameof(GetSiteCheck),
            new { siteId, id = siteCheck.Id },
            siteCheck);
    }

    [HttpDelete]
    public async Task<ActionResult> DeleteAllSiteChecks(
        [FromRoute] int siteId)
    {
        var site = await _dbContext.Sites
            .FirstOrDefaultAsync(
                s => s.Id == siteId,
                CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        var siteChecks = _dbContext.SiteChecks
            .Where(sc => sc.SiteId == siteId);
        _dbContext.SiteChecks.RemoveRange(siteChecks);
        await _dbContext.SaveChangesAsync(CancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteSiteCheck(
        [FromRoute] int siteId,
        [FromRoute] int id)
    {
        var siteCheck = await _dbContext.SiteChecks
            .FirstOrDefaultAsync(
                sc => sc.Id == id && sc.SiteId == siteId,
                CancellationToken);
        if (siteCheck == null)
        {
            return NotFound();
        }

        _dbContext.SiteChecks.Remove(siteCheck);
        await _dbContext.SaveChangesAsync(CancellationToken);

        return NoContent();
    }
}
