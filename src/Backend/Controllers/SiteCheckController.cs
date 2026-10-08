using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Services.CheckQueue;
using SiteChecker.Database;
using SiteChecker.Database.Extensions;
using SiteChecker.Database.Model;

namespace SiteChecker.Backend.Controllers;

[Route("api/Site/{siteId}/check")]
[ApiController]
public class SiteCheckController(
    SiteCheckerDbContext dbContext,
    SiteCheckRunner runner)
    : ControllerBase
{
    private CancellationToken CancellationToken => HttpContext.RequestAborted;
    private readonly SiteCheckerDbContext _dbContext = dbContext;
    private readonly SiteCheckRunner _runner = runner;

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
        var siteCheck = await _dbContext.SiteChecks
            .AsNoTracking()
            .Include(sc => sc.Screenshot)
            .FirstOrDefaultAsync(
                sc => sc.Id == id && sc.SiteId == siteId,
                CancellationToken);
        return this.OkOrNotFound(siteCheck?.Screenshot);
    }

    /// <summary>
    /// Requests a check for the site, or returns the site's open check if it already has one.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SiteCheck>> CreateSiteCheck(
        [FromRoute] int siteId)
    {
        var siteCheck = await _runner.RequestCheckAsync(siteId, CancellationToken);
        if (siteCheck == null)
        {
            return NotFound();
        }

        return CreatedAtAction(
            nameof(GetSiteCheck),
            new { siteId, id = siteCheck.Id },
            siteCheck);
    }

    /// <summary>
    /// Records a Baseline Reset: a Succeeded check without a scrape, so the next check notifies Updated.
    /// </summary>
    [HttpPost(nameof(ResetBaseline))]
    public async Task<ActionResult<SiteCheck>> ResetBaseline(
        [FromRoute] int siteId)
    {
        var siteCheck = await _runner.RecordBaselineResetAsync(siteId, CancellationToken);
        if (siteCheck == null)
        {
            return NotFound();
        }

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
