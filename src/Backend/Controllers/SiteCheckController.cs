using Microsoft.AspNetCore.Mvc;
using SiteChecker.Application.UseCases;
using SiteChecker.Backend.Extensions;
using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;

namespace SiteChecker.Backend.Controllers;

[Route("api/Site/{siteId}/check")]
[ApiController]
public class SiteCheckController(
    CreateSiteCheckUseCase createSiteCheck,
    ManageSitesUseCase manageSites)
    : ControllerBase
{
    private CancellationToken CancellationToken => HttpContext.RequestAborted;
    private readonly CreateSiteCheckUseCase _createSiteCheck = createSiteCheck;
    private readonly ManageSitesUseCase _manageSites = manageSites;

    [HttpGet]
    public async Task<ActionResult<PagedResponse<SiteCheck>>> GetAllSiteChecks(
        [FromRoute] int siteId,
        [FromQuery] int pageNumber = 0,
        [FromQuery] int pageSize = 10)
    {
        var siteChecks = await _manageSites.GetSiteChecksPagedAsync(
            siteId, pageNumber, pageSize, CancellationToken);
        return Ok(siteChecks);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SiteCheck>> GetSiteCheck(
        [FromRoute] int siteId,
        [FromRoute] int id)
    {
        var siteCheck = await _manageSites.GetSiteCheckAsync(siteId, id, CancellationToken);
        return this.OkOrNotFound(siteCheck);
    }

    [HttpGet("{id}/screenshot")]
    public async Task<ActionResult<SiteCheckScreenshot>> GetScreenshot(
        [FromRoute] int siteId,
        [FromRoute] int id)
    {
        var screenshot = await _manageSites.GetScreenshotAsync(siteId, id, CancellationToken);
        return this.OkOrNotFound(screenshot);
    }

    [HttpPost]
    public async Task<ActionResult<SiteCheck>> CreateSiteCheck(
        [FromRoute] int siteId)
    {
        var siteCheck = await _createSiteCheck.ExecuteAsync(siteId, CancellationToken);
        if (siteCheck == null)
        {
            return NotFound();
        }

        return CreatedAtAction(
            nameof(GetSiteCheck),
            new { siteId, id = siteCheck.Id },
            siteCheck);
    }

    [HttpPost(nameof(CreateEmptyCheck))]
    public async Task<ActionResult<SiteCheck>> CreateEmptyCheck(
        [FromRoute] int siteId)
    {
        var siteCheck = await _createSiteCheck.ExecuteEmptyAsync(siteId, CancellationToken);
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
        var site = await _manageSites.GetSiteAsync(siteId, CancellationToken);
        if (site == null)
        {
            return NotFound();
        }

        await _manageSites.DeleteSiteChecksAsync(siteId, CancellationToken);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteSiteCheck(
        [FromRoute] int siteId,
        [FromRoute] int id)
    {
        var found = await _manageSites.DeleteSiteCheckAsync(siteId, id, CancellationToken);
        return found ? NoContent() : NotFound();
    }
}
