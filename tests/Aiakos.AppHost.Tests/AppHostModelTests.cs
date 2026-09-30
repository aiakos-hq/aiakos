using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiakos.AppHost.Tests;

/// <summary>
/// Model tests of the dev AppHost (spec 0001 Test plan): the app model is built, nothing is started.
/// </summary>
public sealed class AppHostModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<IDistributedApplicationTestingBuilder> CreateAsync(params string[] args) =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Aiakos_AppHost>(args, Ct);

    private static IResource Get(IDistributedApplicationTestingBuilder builder, string name) =>
        Assert.Single(builder.Resources, r => r.Name == name);

    private static async Task<Dictionary<string, object>> RawEnvironmentAsync(
        IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, [], Ct);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    private static async Task<IExecutionConfigurationResult> ResolveAsync(
        IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource)
            .WithArgumentsConfig()
            .WithEnvironmentVariablesConfig()
            .BuildAsync(builder.ExecutionContext, NullLogger.Instance, Ct);
        Assert.Null(result.Exception);
        return result;
    }

    private static string[] Arguments(IExecutionConfigurationResult config) => [.. config.Arguments.Select(a => a.Value)];

    private static void AssertWaitsFor(IResource resource, string dependency, WaitType waitType)
    {
        Assert.Contains(
            resource.Annotations.OfType<WaitAnnotation>(),
            w => w.Resource.Name == dependency && w.WaitType == waitType);
    }

    [Fact]
    public async Task TheModelContainsTheSixResources()
    {
        var builder = await CreateAsync();

        Assert.IsType<PostgresServerResource>(Get(builder, "postgres"));
        Assert.IsType<PostgresDatabaseResource>(Get(builder, "aiakos"));
        Assert.IsType<ProjectResource>(Get(builder, "orchestrator"));
        Assert.IsType<ExecutableResource>(Get(builder, "node-publish"));
        Assert.IsType<Hosting.Wsl.WslExecutableResource>(Get(builder, "node-install"));
        Assert.IsType<Hosting.Wsl.WslExecutableResource>(Get(builder, "node-wsl"));

        // Besides these, the model holds parameters and Aspire's own hidden resources (Aspire 13.5
        // adds a hidden "<project>-rebuilder" per project resource).
        var visible = builder.Resources
            .Where(r => r is not ParameterResource && !r.Annotations.Any(a => a.GetType().Name == "HiddenAnnotation"))
            .Select(r => r.Name)
            .Order();
        Assert.Equal(["aiakos", "node-install", "node-publish", "node-wsl", "orchestrator", "postgres"], visible);
    }

    [Fact]
    public async Task TheResourcesWaitForTheirDependencies()
    {
        var builder = await CreateAsync();

        AssertWaitsFor(Get(builder, "orchestrator"), "aiakos", WaitType.WaitUntilHealthy);
        AssertWaitsFor(Get(builder, "node-install"), "node-publish", WaitType.WaitForCompletion);
        AssertWaitsFor(Get(builder, "node-wsl"), "node-install", WaitType.WaitForCompletion);
        AssertWaitsFor(Get(builder, "node-wsl"), "orchestrator", WaitType.WaitUntilHealthy);
    }

    [Fact]
    public async Task PostgresIsPersistentWithAPinnedMajorAndAnInstanceVolume()
    {
        var builder = await CreateAsync();
        var postgres = Get(builder, "postgres");

        var image = Assert.Single(postgres.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("18", image.Tag);
        Assert.Equal(ContainerLifetime.Persistent, Assert.Single(postgres.Annotations.OfType<ContainerLifetimeAnnotation>()).Lifetime);
        var volume = Assert.Single(postgres.Annotations.OfType<ContainerMountAnnotation>(), m => m.Type == ContainerMountType.Volume);
        Assert.Equal("aiakos-dev-pgdata", volume.Source);
        // Postgres 18 images keep data under /var/lib/postgresql (PGDATA /var/lib/postgresql/18/docker).
        Assert.Equal("/var/lib/postgresql", volume.Target);
    }

    [Fact]
    public async Task ThePostgresVolumeIsNamedAfterTheInstance()
    {
        var builder = await CreateAsync("--Aiakos:Instance=second");

        var volume = Assert.Single(Get(builder, "postgres").Annotations.OfType<ContainerMountAnnotation>(), m => m.Type == ContainerMountType.Volume);
        Assert.Equal("aiakos-second-pgdata", volume.Source);
    }

    [Theory]
    [InlineData("postgres-password")]
    [InlineData("node-token")]
    public async Task GeneratedSecretsArePersistedInUserSecrets(string name)
    {
        var first = GetSecret(await CreateAsync(), name);
        var second = GetSecret(await CreateAsync(), name);

        Assert.True(first.Secret);
        // UserSecretsParameterDefault is internal in Aspire 13.5: it wraps the generated default,
        // reads Parameters:<name> from the AppHost's user secrets and writes it there on first use.
        Assert.Equal("UserSecretsParameterDefault", first.Default?.GetType().Name);
        // A second AppHost start sees the same value, so the Postgres volume keeps matching.
        var value = await first.GetValueAsync(Ct);
        Assert.False(string.IsNullOrEmpty(value));
        Assert.Equal(value, await second.GetValueAsync(Ct));
    }

    private static ParameterResource GetSecret(IDistributedApplicationTestingBuilder builder, string name) =>
        name == "postgres-password"
            ? Assert.IsType<PostgresServerResource>(Get(builder, "postgres")).PasswordParameter
            : Assert.IsType<ParameterResource>(Get(builder, name));

    [Fact]
    public async Task TheOrchestratorHasAnUnproxiedGrpcEndpointOnThePortBaseAndAnHttpHealthEndpoint()
    {
        var builder = await CreateAsync();
        var orchestrator = Get(builder, "orchestrator");

        var endpoints = orchestrator.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);
        Assert.Equal(["grpc", "http"], endpoints.Keys.Order());
        Assert.Equal(5180, endpoints["grpc"].Port);
        Assert.False(endpoints["grpc"].IsProxied);
        Assert.Equal("http", endpoints["grpc"].UriScheme);
        Assert.True(endpoints["http"].IsProxied);
        Assert.Null(endpoints["http"].Port);
        Assert.Single(orchestrator.Annotations.OfType<HealthCheckAnnotation>());

        var env = await RawEnvironmentAsync(builder, orchestrator);
        Assert.Equal("5180", env["Aiakos__Orchestrator__GrpcPort"]);
        Assert.Equal("dev", env["Aiakos__Instance"]);
        Assert.Equal("wsl-local", env["Aiakos__Nodes__0__Id"]);
        Assert.Equal("node-token", Assert.IsType<ParameterResource>(env["Aiakos__Nodes__0__Token"]).Name);
        Assert.Contains("ConnectionStrings__aiakos", env.Keys);
    }

    [Fact]
    public async Task NodePublishPublishesTheNodeSelfContainedFromTheRepositoryRoot()
    {
        var builder = await CreateAsync();
        var publish = Assert.IsType<ExecutableResource>(Get(builder, "node-publish"));

        Assert.Equal("dotnet", publish.Command);
        Assert.True(File.Exists(Path.Combine(publish.WorkingDirectory, "Aiakos.slnx")));
        Assert.Equal(
            ["publish", "src/Aiakos.Node", "-c", "Debug", "-r", "linux-x64", "--self-contained", "-o", "artifacts/node/linux-x64"],
            Arguments(await ResolveAsync(builder, publish)));
    }

    [Fact]
    public async Task NodeInstallCopiesThePublishOutputIntoTheWslHome()
    {
        var builder = await CreateAsync();
        var install = Assert.IsType<Hosting.Wsl.WslExecutableResource>(Get(builder, "node-install"));
        var publish = Assert.IsType<ExecutableResource>(Get(builder, "node-publish"));

        var args = Arguments(await ResolveAsync(builder, install));

        var publishDir = Path.GetFullPath(Path.Combine(publish.WorkingDirectory, "artifacts", "node", "linux-x64"));
        Assert.Equal(
            ["-d", "Ubuntu", "--cd", "~", "--exec", "/bin/sh", "-c", NodeDeployment.WslInstallScript, "sh", publishDir, ".aiakos-dev"],
            args);
        Assert.Equal(
            "set -eu\n"
            + "src=\"$(wslpath -u \"$1\")\"; home=\"$HOME/$2\"\n"
            + "mkdir -p \"$home\"; rm -rf \"$home/node.staging\"\n"
            + "cp -r \"$src\" \"$home/node.staging\"; chmod +x \"$home/node.staging/aiakos-node\"\n"
            + "rm -rf \"$home/node\"; mv \"$home/node.staging\" \"$home/node\"",
            NodeDeployment.WslInstallScript);
    }

    [Fact]
    public async Task NodeWslRunsTheInstalledNodeWithTheNodeEnvironment()
    {
        var builder = await CreateAsync();
        var node = Get(builder, "node-wsl");

        var config = await ResolveAsync(builder, node);

        Assert.Equal(["-d", "Ubuntu", "--cd", "~", "--exec", ".aiakos-dev/node/aiakos-node"], Arguments(config));
        var env = config.EnvironmentVariables.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Assert.Equal("http://127.0.0.1:5180", env["AIAKOS_ORCHESTRATOR_URL"]);
        Assert.Equal(".aiakos-dev", env["AIAKOS_HOME"]);
        Assert.Equal("wsl-local", env["AIAKOS_NODE_ID"]);
        Assert.Equal("dev", env["AIAKOS_INSTANCE"]);
        Assert.Equal("Development", env["DOTNET_ENVIRONMENT"]);
        Assert.False(string.IsNullOrEmpty(env["AIAKOS_NODE_TOKEN"]));
        Assert.StartsWith("http://127.0.0.1:", env["OTEL_EXPORTER_OTLP_ENDPOINT"], StringComparison.Ordinal);

        var forwarded = env["WSLENV"].Split(':');
        foreach (var name in new[]
        {
            "AIAKOS_ORCHESTRATOR_URL", "AIAKOS_HOME", "AIAKOS_NODE_ID", "AIAKOS_NODE_TOKEN", "AIAKOS_INSTANCE",
            "DOTNET_ENVIRONMENT", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_SERVICE_NAME",
        })
        {
            Assert.Contains(name + "/u", forwarded);
        }
    }

    [Fact]
    public async Task NodeWslTakesTheOrchestratorUrlFromThePortBase()
    {
        var builder = await CreateAsync("--Aiakos:PortBase=6180");

        var config = await ResolveAsync(builder, Get(builder, "node-wsl"));
        var grpc = Get(builder, "orchestrator").Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "grpc");

        Assert.Equal("http://127.0.0.1:6180", config.EnvironmentVariables.Single(kv => kv.Key == "AIAKOS_ORCHESTRATOR_URL").Value);
        Assert.Equal(6180, grpc.Port);
    }

    [Theory]
    [InlineData("--Aiakos:Instance=release", "Aiakos:Instance")]
    [InlineData("--Aiakos:Wsl:Home=.aiakos", "Aiakos:Wsl:Home")]
    [InlineData("--Aiakos:PortBase=7180", "Aiakos:PortBase")]
    public async Task TheAppHostRefusesTheReleasedInstanceValues(string arg, string key)
    {
        var ex = await Record.ExceptionAsync(() => CreateAsync(arg));

        Assert.NotNull(ex);
        var guard = Unwrap(ex).OfType<InstanceGuardException>().FirstOrDefault();
        Assert.True(guard is not null, $"Expected an {nameof(InstanceGuardException)}, got: {ex}");
        Assert.Equal(key, guard.Key);
        Assert.Contains(key, guard.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<Exception> Unwrap(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Unwrap))
                {
                    yield return inner;
                }
            }
        }
    }
}
