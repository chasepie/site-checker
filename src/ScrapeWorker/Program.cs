using dotenv.net;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;

namespace SiteChecker.ScrapeWorker;

/// <summary>
/// The Scrape Worker: runs scrapes for the app in a container of their own, with no secrets but the
/// Browserless token, no database and no Docker access, so a script can reach none of them. The app
/// talks to it through <c>RemoteScraperService</c>; see <see cref="ScrapeWorkerEndpoints"/>.
/// </summary>
public sealed class Program
{
    public static async Task Main(string[] args)
    {
        DotEnv.Fluent()
            .WithEnvFiles()
            .WithProbeForEnv()
            .WithOverwriteExistingVars()
            .WithoutExceptions()
            .Load();

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddScrapeWorker();

        var app = builder.Build();
        app.MapScrapeWorker();
        ValidateScraperServices(app);

        await app.RunAsync();
    }

    /// <summary>
    /// Fails startup on invalid scrape timeouts, and builds the script compiler's references once,
    /// up front, rather than on the first scrape.
    /// </summary>
    private static void ValidateScraperServices(WebApplication app)
    {
        app.Services.GetRequiredService<ScrapeTimeouts>();
        app.Services.GetRequiredService<IScriptCompiler>();
    }
}
