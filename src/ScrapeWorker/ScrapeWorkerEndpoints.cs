using SiteChecker.Scraper;

namespace SiteChecker.ScrapeWorker;

/// <summary>
/// The worker's API, for the app only. <c>/scrape</c> and <c>/scripts</c> require
/// <see cref="ScrapeWorkerSecret"/>; <c>/healthz</c> doesn't, so Docker can check it.
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
        /// <exception cref="InvalidOperationException"><c>SCRAPE_WORKER_SECRET</c> isn't set outside
        /// Development.</exception>
        public WebApplication MapScrapeWorker()
        {
            var secret = ScrapeWorkerSecret.Read(app.Configuration);
            if (secret is null)
            {
                if (!app.Environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        $"{ScrapeWorkerSecret.Key} must be set, to the same value as the app's. The worker runs the scripts in its requests, so it only takes requests carrying the secret.");
                }

                app.Logger.LogWarning("{Key} isn't set, so anything that can reach the worker can run scripts in it.", ScrapeWorkerSecret.Key);
            }

            var api = app.MapGroup(string.Empty);
            if (secret is not null)
            {
                api.AddEndpointFilter((context, next) => HasSecret(context.HttpContext, secret)
                    ? next(context)
                    : ValueTask.FromResult<object?>(Results.Unauthorized()));
            }
            api.MapPost(ScrapePath, ScrapeAsync);
            api.MapDelete($"{ScriptsPath}/{{siteId:int}}", EvictScriptAsync);
            app.MapHealthChecks(HealthPath);
            return app;
        }
    }

    private static bool HasSecret(HttpContext httpContext, string secret)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        return authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            && ScrapeWorkerSecret.Matches(secret, authorization[bearer.Length..].Trim());
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
