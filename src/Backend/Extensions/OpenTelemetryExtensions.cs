using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SiteChecker.Backend.Extensions;

public static class OpenTelemetryExtensions
{
    public static void ConfigureLogging(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (string.IsNullOrEmpty(otlpEndpoint))
        {
            Console.WriteLine("OpenTelemetry OTLP endpoint is not configured. Using simple logger");
            builder.Logging.AddSimpleConsole();
            return;
        }

        var environment = builder.Environment.EnvironmentName;

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = environment,
                ["service.namespace"] = nameof(SiteChecker)
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging
                .SetResourceBuilder(resourceBuilder)
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(otlpEndpoint);
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(otlpEndpoint);
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    });
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(otlpEndpoint);
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    });
            });
    }
}
