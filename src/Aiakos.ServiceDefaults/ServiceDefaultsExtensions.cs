using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aiakos.ServiceDefaults;

/// <summary>Registers telemetry and health checks shared by Aiakos hosts (spec 0001 R41, R42).</summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Tag of health checks that report process liveness (<c>/alive</c>).</summary>
    public const string LiveTag = "live";

    /// <summary>
    /// Adds OpenTelemetry logs, traces and metrics for the <c>Aiakos.*</c> sources and meters, a
    /// <c>self</c> liveness check, and a bounded telemetry flush on shutdown. Telemetry is exported
    /// through OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.
    /// </summary>
    public static TBuilder AddAiakosServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddAttributes(ResourceAttributes(builder.Configuration)))
            .WithTracing(tracing => tracing
                .AddSource("Aiakos.*")
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter("Aiakos.*")
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        builder.Services.Configure<TelemetryShutdownOptions>(
            builder.Configuration.GetSection(TelemetryShutdownOptions.Section));
        builder.Services.AddHostedService<TelemetryShutdownService>();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    /// <summary>
    /// Resource attributes: <c>aiakos.instance</c> from <c>Aiakos:Instance</c> or
    /// <c>AIAKOS_INSTANCE</c>, and <c>aiakos.node.id</c> from <c>AIAKOS_NODE_ID</c> when set.
    /// <c>service.name</c> comes from <c>OTEL_SERVICE_NAME</c> through the SDK.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, object>> ResourceAttributes(IConfiguration configuration)
    {
        var instance = configuration["Aiakos:Instance"] ?? configuration["AIAKOS_INSTANCE"];
        if (!string.IsNullOrWhiteSpace(instance))
        {
            yield return new("aiakos.instance", instance);
        }

        var nodeId = configuration["AIAKOS_NODE_ID"];
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            yield return new("aiakos.node.id", nodeId);
        }
    }
}
