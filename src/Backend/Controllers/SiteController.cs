using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteChecker.Backend.Extensions;
using SiteChecker.Backend.Models;
using SiteChecker.Backend.Services.Security;
using SiteChecker.Backend.Services.Sites;
using SiteChecker.Backend.Services.TestRuns;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;

namespace SiteChecker.Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class SiteController(
    SiteCheckerDbContext dbContext,
    SiteValidator validator,
    IScraperService scraperService,
    TestRunService testRuns,
    TimeProvider timeProvider)
    : ControllerBase
{
    private readonly SiteCheckerDbContext _dbContext = dbContext;
    private readonly SiteValidator _validator = validator;
    private readonly IScraperService _scraperService = scraperService;
    private readonly TestRunService _testRuns = testRuns;
    private readonly TimeProvider _timeProvider = timeProvider;

    private CancellationToken CancellationToken => HttpContext.RequestAborted;

    [HttpGet]
    public async Task<ActionResult<List<Site>>> GetAllSites()
    {
        var sites = await _dbContext.Sites
            .AsNoTracking()
            .Include(s => s.SiteChecks
                .OrderByDescending(sc => sc.StartDate)
                .Take(1))
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

    /// <summary>
    /// Creates a Site with its Scraper. A script that doesn't compile is rejected with its errors.
    /// Requires the admin token.
    /// </summary>
    [Authorize(Policy = AdminToken.PolicyName)]
    [HttpPost]
    public async Task<ActionResult<Site>> CreateSite([FromBody] SiteRequest siteRequest)
    {
        var validation = _validator.ValidateSite(siteRequest, existing: null);
        if (!validation.IsValid)
        {
            return BadRequest(validation);
        }

        var site = new Site { Name = siteRequest.Name, Url = siteRequest.Url };
        site.Update(siteRequest);
        ApplyScraper(site, siteRequest.Scraper);
        _dbContext.Sites.Add(site);
        await _dbContext.SaveChangesAsync(CancellationToken);

        return CreatedAtAction(nameof(GetSite), new { id = site.Id }, site);
    }

    /// <summary>
    /// Updates a Site's settings and Scraper. Without a script, the Site keeps its current one.
    /// Requires the admin token.
    /// </summary>
    [Authorize(Policy = AdminToken.PolicyName)]
    [HttpPut("{id}")]
    public async Task<ActionResult<Site>> UpdateSite(
        [FromRoute] int id,
        [FromBody] SiteRequest siteRequest)
    {
        if (id != siteRequest.Id)
        {
            return BadRequest();
        }

        var site = await _dbContext.Sites
            .Include(s => s.SiteScript)
            .FirstOrDefaultAsync(s => s.Id == id, CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        var validation = _validator.ValidateSite(siteRequest, site);
        if (!validation.IsValid)
        {
            return BadRequest(validation);
        }

        site.Update(siteRequest);
        var scriptReplaced = ApplyScraper(site, siteRequest.Scraper);
        await _dbContext.SaveChangesAsync(CancellationToken);

        if (scriptReplaced)
        {
            await _scraperService.EvictScriptAsync(site.Id, CancellationToken);
        }
        return Ok(site);
    }

    /// <summary>
    /// Deletes a Site, with its Site Checks, screenshots and script.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteSite([FromRoute] int id)
    {
        var site = await _dbContext.Sites
            .FirstOrDefaultAsync(s => s.Id == id, CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        // The required-FK cascade deletes the Site's checks, screenshots and script in the
        // database, so clients only hear that the Site was deleted.
        _dbContext.Sites.Remove(site);
        await _dbContext.SaveChangesAsync(CancellationToken);
        await _scraperService.EvictScriptAsync(id, CancellationToken);

        return NoContent();
    }

    /// <summary>
    /// The Site's script source, for viewing and downloading.
    /// </summary>
    [HttpGet("{id}/script")]
    public async Task<ActionResult<SiteScript>> GetSiteScript([FromRoute] int id)
    {
        var script = await _dbContext.SiteScripts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SiteId == id, CancellationToken);
        return this.OkOrNotFound(script);
    }

    /// <summary>
    /// Starts a Test Run of an unsaved Scraper and Site settings. The result is sent to the given
    /// SignalR connection only, as OnTestRunCompleted. Requires the admin token.
    /// </summary>
    [Authorize(Policy = AdminToken.PolicyName)]
    [HttpPost("test-run")]
    public ActionResult StartTestRun([FromBody] TestRunRequest testRunRequest)
    {
        var validation = _validator.ValidateTestRun(testRunRequest);
        if (!validation.IsValid)
        {
            return BadRequest(validation);
        }

        _testRuns.Start(testRunRequest);
        return Accepted();
    }

    /// <summary>
    /// Sets the Site's Scraper from a validated request.
    /// </summary>
    /// <returns>Whether the script's source changed.</returns>
    private bool ApplyScraper(Site site, ScraperRequest scraper)
    {
        site.Scraper.Kind = scraper.Kind;
        if (scraper.Script is not { } upload)
        {
            return false;
        }

        var sourceHash = ScriptSource.Hash(upload.Source);
        if (site.Scraper.Script is { } current
            && string.Equals(current.SourceHash, sourceHash, StringComparison.Ordinal))
        {
            current.FileName = upload.FileName;
            return false;
        }

        site.Scraper.Script = new ScriptScraper
        {
            FileName = upload.FileName,
            SourceHash = sourceHash,
            UploadedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        if (site.SiteScript is { } siteScript)
        {
            siteScript.Source = upload.Source;
        }
        else
        {
            site.SiteScript = new SiteScript { Source = upload.Source };
        }
        return true;
    }
}
