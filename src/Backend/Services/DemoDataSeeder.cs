using Microsoft.EntityFrameworkCore;
using SiteChecker.Database;
using SiteChecker.Database.Model;
using SiteChecker.Scraper.Scripts;

namespace SiteChecker.Backend.Services;

/// <summary>
/// Seeds the demo Sites (PIA Location and Bot Detection) into an empty database, with the scripts
/// from <c>samples/DemoScrapers</c>. It never updates or deletes, so Sites created at runtime are
/// never touched. Controlled by <c>SEED_DEMO_DATA</c>; when that's unset, it seeds only in
/// Development.
/// </summary>
public sealed class DemoDataSeeder(
    SiteCheckerDbContext dbContext,
    IConfiguration configuration,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<DemoDataSeeder> logger)
{
    public const string SeedDemoDataKey = "SEED_DEMO_DATA";

    private readonly SiteCheckerDbContext _dbContext = dbContext;
    private readonly IConfiguration _configuration = configuration;
    private readonly IHostEnvironment _environment = environment;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<DemoDataSeeder> _logger = logger;

    public bool IsEnabled => bool.TryParse(_configuration[SeedDemoDataKey], out var enabled)
        ? enabled
        : _environment.IsDevelopment();

    /// <summary>
    /// Adds the demo Sites if seeding is on and there are no Sites at all.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled || await _dbContext.Sites.AnyAsync(cancellationToken))
        {
            return;
        }

        var uploadedAt = _timeProvider.GetUtcNow().UtcDateTime;
        _dbContext.Sites.AddRange(
            DemoSite("PIA Location", "https://www.privateinternetaccess.com/what-is-my-ip", useVpn: true, "PiaLocation.cs", uploadedAt),
            DemoSite("Bot Detection", "https://www.browserscan.net/bot-detection", useVpn: false, "BotDetection.cs", uploadedAt));
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded the demo Sites.");
    }

    private static Site DemoSite(string name, string url, bool useVpn, string scriptFileName, DateTime uploadedAt)
    {
        var source = ReadDemoScript(scriptFileName);
        return new Site
        {
            Name = name,
            Url = new Uri(url),
            UseVpn = useVpn,
            // Bot Detection's script relies on this for its success screenshot.
            AlwaysTakeScreenshot = true,
            Scraper = new ScraperDefinition
            {
                Kind = ScraperKind.Script,
                Script = new ScriptScraper
                {
                    FileName = scriptFileName,
                    SourceHash = ScriptSource.Hash(source),
                    UploadedAt = uploadedAt,
                },
            },
            SiteScript = new SiteScript { Source = source },
        };
    }

    /// <summary>
    /// Reads a script from <c>samples/DemoScrapers</c>, which the Backend embeds (see Backend.csproj).
    /// </summary>
    private static string ReadDemoScript(string fileName)
    {
        var resourceName = $"DemoScrapers.{fileName}";
        using var stream = typeof(DemoDataSeeder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The demo script '{resourceName}' isn't embedded in the Backend.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
