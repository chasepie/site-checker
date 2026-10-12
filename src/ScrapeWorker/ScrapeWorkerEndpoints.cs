using SiteChecker.Scraper;

namespace SiteChecker.ScrapeWorker;

/// <summary>
/// The worker's API, for the app only:
/// <list type="bullet">
/// <item><c>POST /scrape</c> runs a <see cref="ScrapeRequest"/> and returns its <see cref="ScrapeResult"/>,
/// both as <see cref="ScrapeJson"/>. It answers 503 while a run abandoned at its timeout is still going,
/// since the worker is about to stop.</item>
/// <item><c>DELETE /scripts/{siteId}</c> unloads a Site's compiled script.</item>
/// <item><c>GET /healthz</c> is unhealthy while a run abandoned at its timeout is still going.</item>
/// </list>
/// </summary>
public static class ScrapeWorkerEndpoints
{
    public const string ScrapePath = "/" + ScrapeWorkerPaths.Scrape;
    public const string ScriptsPath = "/" + ScrapeWorkerPaths.Scripts;
    public const string HealthPath = "/" + ScrapeWorkerPaths.Health;

    extension(WebApplication app)
    {
        public WebApplication MapScrapeWorker()
        {
            app.MapPost(ScrapePath, ScrapeAsync);
            app.MapDelete($"{ScriptsPath}/{{siteId:int}}", EvictScriptAsync);
            app.MapHealthChecks(HealthPath);
            return app;
        }
    }

    private static async Task<IResult> ScrapeAsync(
        HttpContext httpContext,
        IScraperService scraperService,
        IAbandonedRunMonitor abandonedRuns)
    {
        if (abandonedRuns.HasAbandonedRuns)
        {
            return Results.Problem(
                "A timed-out scrape is still running, so the worker is about to restart.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var cancellationToken = httpContext.RequestAborted;
        var request = await httpContext.Request.ReadFromJsonAsync<ScrapeRequest>(ScrapeJson.Options, cancellationToken);
        if (request is null)
        {
            return Results.BadRequest();
        }

        var result = await scraperService.ScrapeAsync(request, cancellationToken);
        return Results.Json(result, ScrapeJson.Options);
    }

    private static async Task<IResult> EvictScriptAsync(
        int siteId,
        HttpContext httpContext,
        IScraperService scraperService)
    {
        await scraperService.EvictScriptAsync(siteId, httpContext.RequestAborted);
        return Results.NoContent();
    }
}
