namespace SiteChecker.Backend.Services;

public class HealthCheckService : BackgroundService
{
    public const string HealthChecksURL = "HEALTHCHECKS_URL";

    private readonly ILogger<HealthCheckService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _healthChecksUrl;

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        var url = configuration[HealthChecksURL];
        ArgumentException.ThrowIfNullOrEmpty(url, HealthChecksURL);
        _healthChecksUrl = url;

        _logger.LogInformation("Healthcheck Service initialized");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _httpClient.GetAsync(_healthChecksUrl, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check HTTP request failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}

public static class HealthCheckServiceExtensions
{
    public static IServiceCollection TryAddHealthCheckService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var url = configuration[HealthCheckService.HealthChecksURL];
        if (string.IsNullOrEmpty(url))
        {
            return services;
        }

        return services.AddHealthCheckService(configuration);
    }

    public static IServiceCollection AddHealthCheckService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var url = configuration[HealthCheckService.HealthChecksURL];
        ArgumentException.ThrowIfNullOrEmpty(url, HealthCheckService.HealthChecksURL);

        services.AddHttpClient();
        return services.AddHostedService<HealthCheckService>();
    }
}
