using System.Globalization;

using Aiakos.Data;
using Aiakos.Orchestrator;
using Aiakos.Orchestrator.Link;
using Aiakos.ServiceDefaults;

using Akka.Hosting;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Migrations run first: this hosted service must be registered before every other one (R28).
builder.Services.AddSingleton<MigrationState>();
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddHostedService<MigrationHostedService>();

builder.AddAiakosServiceDefaults();

// ASP.NET Core instrumentation lives here, not in ServiceDefaults (R41). Health probes are not traced.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(options =>
        options.Filter = static context =>
            !context.Request.Path.StartsWithSegments(Endpoints.Health, StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.StartsWithSegments(Endpoints.Alive, StringComparison.OrdinalIgnoreCase)))
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

// Options: Aiakos:Instance and Aiakos:Nodes are bound but not validated here (spec 0002 owns tokens).
var aiakosSection = builder.Configuration.GetSection(AiakosOptions.Section);
builder.Services.Configure<AiakosOptions>(aiakosSection);
builder.Services.Configure<NodeLinkOptions>(static _ => { });
builder.Services.TryAddSingleton<INodeLinkApplication, EmptyNodeLinkApplication>();
builder.Services.AddSingleton(static _ => TimeProvider.System);
builder.Services.AddSingleton<NodeTokenRegistry>(services =>
    new NodeTokenRegistry(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiakosOptions>>().Value.Nodes));
builder.Services.AddSingleton<NodeLinkRegistry>();
var grpcPort = ReadGrpcPort(builder.Configuration);

// Per-endpoint protocols (R13, R14): the endpoint on the gRPC port is HTTP/2 only (h2c); all other
// endpoints keep Kestrel's default Http1AndHttp2, so Aspire's HTTP/1.1 health probe works. The
// defaults callback runs for every endpoint Kestrel binds, including those from ASPNETCORE_URLS,
// which is how Aspire passes a project's endpoints. Protocols are deliberately not set through
// Kestrel__EndpointDefaults__Protocols, which would apply to both endpoints.
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var configuredAddresses = context.Configuration.GetSection("Kestrel:Endpoints").GetChildren()
        .Select(static endpoint => endpoint["Url"])
        .Where(static url => !string.IsNullOrWhiteSpace(url))
        .Cast<string>()
        .ToList();
    var serverUrls = context.Configuration[WebHostDefaults.ServerUrlsKey];
    if (!string.IsNullOrWhiteSpace(serverUrls))
        configuredAddresses.AddRange(serverUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    NodeLinkEndpointPolicy.ValidateConfigured(configuredAddresses, grpcPort);

    kestrel.ConfigureEndpointDefaults(listen =>
    {
        NodeLinkEndpointPolicy.ValidateResolved(listen.EndPoint, grpcPort);
        if (grpcPort is { } port && listen.IPEndPoint?.Port == port)
            listen.Protocols = HttpProtocols.Http2;
    });
});

builder.AddNpgsqlDataSource("aiakos");
builder.Services.AddSingleton<TenantRepository>();

// The ActorSystem hosts live entities only; the skeleton registers no actors and uses no
// remoting, clustering or persistence (R27). It stops with the host.
builder.Services.AddAkka("aiakos", akka => akka.ConfigureLoggers(loggers =>
{
    loggers.ClearLoggers();
    loggers.AddLoggerFactory();
}));

builder.Services.AddGrpc();
builder.Services.AddHealthChecks()
    .AddCheck<MigrationsHealthCheck>(MigrationsHealthCheck.Name);

// The gRPC health service reports the same checks as /health (R29).
builder.Services.AddGrpcHealthChecks(options =>
{
    options.Services.Clear();
    options.Services.Map("", static _ => true);
});

var app = builder.Build();

app.MapHealthChecks(Endpoints.Health);
app.MapHealthChecks(Endpoints.Alive, new HealthCheckOptions
{
    Predicate = static registration => registration.Tags.Contains(ServiceDefaultsExtensions.LiveTag),
});

var grpcHealth = app.MapGrpcHealthChecksService();
if (grpcPort is { } p)
{
    // gRPC is reachable only on the gRPC endpoint (R14).
    grpcHealth.RequireHost($"*:{p.ToString(CultureInfo.InvariantCulture)}");
}

var nodeLink = app.MapGrpcService<NodeLinkService>();
if (grpcPort is { } nodeLinkPort)
    nodeLink.RequireHost($"*:{nodeLinkPort.ToString(CultureInfo.InvariantCulture)}");

// A failed migration throws out of RunAsync after being logged with its script name, so the
// process exits non-zero and never serves on a partially migrated database (R28). It is not
// caught here: catching it would also hide the failure from WebApplicationFactory in tests.
await app.RunAsync();

static int? ReadGrpcPort(IConfiguration configuration)
{
    var raw = configuration[OrchestratorEndpointOptions.GrpcPortKey];
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException(
            $"Configuration '{OrchestratorEndpointOptions.GrpcPortKey}' must be a port number (1-65535), got '{raw}'.");
    }

    return port;
}

/// <summary>Entry point; public so tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;

/// <summary>HTTP endpoint paths.</summary>
internal static class Endpoints
{
    public const string Health = "/health";

    public const string Alive = "/alive";
}
