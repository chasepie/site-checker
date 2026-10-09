using Microsoft.Extensions.Diagnostics.HealthChecks;
using SiteChecker.Scraper;

namespace SiteChecker.ScrapeWorker;

public static class ScrapeWorkerServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The scrape pipeline, with a monitor that stops the worker when a timed-out run won't end,
        /// so Docker restarts it and the stuck thread goes with it.
        /// </summary>
        public IServiceCollection AddScrapeWorker()
        {
            services.AddSingleton<IAbandonedRunMonitor>(sp =>
            {
                var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
                return new ExitingAbandonedRunMonitor(
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<ExitingAbandonedRunMonitor>>(),
                    () =>
                    {
                        // Not a clean exit, so it's visible in `docker ps -a` and the logs.
                        Environment.ExitCode = 1;
                        lifetime.StopApplication();
                    },
                    Environment.FailFast);
            });
            services.AddScraperServices();
            services.AddHealthChecks().AddCheck<AbandonedRunsHealthCheck>("abandoned-runs");
            return services;
        }
    }

    /// <summary>
    /// Unhealthy while a run abandoned at its timeout is still going: the worker refuses scrapes
    /// until it ends or the worker restarts.
    /// </summary>
    private sealed class AbandonedRunsHealthCheck(IAbandonedRunMonitor abandonedRuns) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(abandonedRuns.HasAbandonedRuns
                ? HealthCheckResult.Unhealthy("A timed-out scrape is still running.")
                : HealthCheckResult.Healthy());
    }
}
