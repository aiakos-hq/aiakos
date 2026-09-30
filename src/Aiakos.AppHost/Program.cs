using System.Globalization;
using System.Reflection;
using Aiakos.AppHost;
using Aiakos.Hosting.Wsl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// The dev AppHost (spec 0001, Design → AppHost). Resource names are normative: tests and docs
// refer to them. Every instance-specific value comes from the "Aiakos" section (R11).
var builder = DistributedApplication.CreateBuilder(args);

var cfg = builder.Configuration.GetSection(AiakosDevOptions.SectionName).Get<AiakosDevOptions>() ?? new AiakosDevOptions();
InstanceGuard.EnsureNotReleased(cfg);                                                      // R18

var repoRoot = NodeDeployment.FindRepositoryRoot(builder.AppHostDirectory);
var grpcPort = cfg.GrpcPort.ToString(CultureInfo.InvariantCulture);

// Postgres: persistent container, named volume per instance, pinned major version (R12). The
// generated password parameter (postgres-password) is persisted in the AppHost's user secrets.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume($"aiakos-{cfg.Instance}-pgdata");
var db = postgres.AddDatabase("aiakos");

// Node token: generated once, persisted in user secrets, transported only (R16; spec 0002 validates).
var nodeToken = builder.AddParameter(
    "node-token",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

// API token for the development CLI: generated once, persisted in user secrets, written to the dev
// Windows home with connection.json (R14, spec 0007 R29). The orchestrator's /v1 API arrives with #15.
var apiToken = builder.AddParameter(
    "api-token",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

// The orchestrator. Endpoints are declared here rather than taken from a launch profile. Both are
// unproxied on fixed ports: "grpc" at the port base (h2c is configured by the orchestrator for the
// endpoint whose port equals Aiakos:Orchestrator:GrpcPort, R13), and "http" at the port base + 1 for
// /health, /alive and the CLI's local API over HTTP/1.1 (R14).
var orchestrator = builder.AddProject<Projects.Aiakos_Orchestrator>("orchestrator", launchProfileName: null)
    .WithHttpEndpoint(name: "http", port: cfg.ApiPort, isProxied: false)
    .WithHttpEndpoint(name: "grpc", port: cfg.GrpcPort, isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Aiakos__Orchestrator__GrpcPort", grpcPort)
    .WithEnvironment("Aiakos__Instance", cfg.Instance)
    .WithEnvironment("Aiakos__Nodes__0__Id", cfg.Wsl.NodeId)
    .WithEnvironment("Aiakos__Nodes__0__Token", nodeToken)
    .WithReference(db)
    .WaitFor(db);                                                                          // R17

// connection.json and the API token file: written once the orchestrator is healthy, deleted on stop.
var connectionFiles = new DevConnectionFiles(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cfg.WindowsHome));
var otlpEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"];
builder.Eventing.Subscribe<ResourceReadyEvent>(orchestrator.Resource, async (_, ct) =>
{
    connectionFiles.WriteApiToken((await apiToken.Resource.GetValueAsync(ct).ConfigureAwait(false))!);
    connectionFiles.WriteConnection(new DevConnection(
        new Uri($"http://127.0.0.1:{cfg.ApiPort.ToString(CultureInfo.InvariantCulture)}"),
        Environment.ProcessId,
        typeof(DevConnectionFiles).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        DateTimeOffset.UtcNow,
        string.IsNullOrWhiteSpace(otlpEndpoint) ? null : new Uri(otlpEndpoint)));
});
builder.Services.AddHostedService(_ => new DevConnectionFilesCleanup(connectionFiles));

// Node deployment: publish on Windows (WSL needs no .NET), then copy into the WSL filesystem (R19–R21).
var publish = builder.AddExecutable("node-publish", "dotnet", repoRoot, NodeDeployment.PublishArguments);

var publishDir = Path.GetFullPath(Path.Combine(repoRoot, NodeDeployment.PublishDirectory));
var install = builder.AddWslExecutable(
        "node-install", cfg.Wsl.Distro, "/bin/sh",
        "-c", NodeDeployment.WslInstallScript, "sh", publishDir, cfg.Wsl.Home)
    .WaitForCompletion(publish);

builder.AddWslExecutable("node-wsl", cfg.Wsl.Distro, NodeDeployment.InstalledNodePath(cfg.Wsl.Home))
    .WithWslOtlpExporter()                                                                 // R25
    .WithEnvironment("AIAKOS_ORCHESTRATOR_URL", $"http://{WslResourceBuilderExtensions.WindowsLoopback}:{grpcPort}") // R13
    .WithEnvironment("AIAKOS_HOME", cfg.Wsl.Home)
    .WithEnvironment("AIAKOS_NODE_ID", cfg.Wsl.NodeId)
    .WithEnvironment("AIAKOS_NODE_TOKEN", nodeToken)
    .WithEnvironment("AIAKOS_INSTANCE", cfg.Instance)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithWslEnvironment()                                                                  // R24
    .WaitForCompletion(install)
    .WaitFor(orchestrator);                                                                // R17

builder.Build().Run();
