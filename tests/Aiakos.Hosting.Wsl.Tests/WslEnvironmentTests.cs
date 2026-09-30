using Aspire.Hosting;

namespace Aiakos.Hosting.Wsl.Tests;

public sealed class WslEnvironmentTests
{
    [Fact]
    public async Task WithWslEnvironmentForwardsExactlyTheDefaultPrefixedVariablesWithU()
    {
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x")
            .WithEnvironment("AIAKOS_HOME", ".aiakos-dev")
            .WithEnvironment("OTEL_SERVICE_NAME", "node")
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment("PATH_EXTRA", "no")
            .WithEnvironment("MY_AIAKOS_THING", "no")
            .WithEnvironment("aiakos_lower", "no")
            .WithWslEnvironment();

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal(
            ["AIAKOS_HOME/u", "DOTNET_ENVIRONMENT/u", "OTEL_SERVICE_NAME/u"],
            TestApp.AddedWslEnvEntries(env["WSLENV"]));
    }

    [Fact]
    public async Task WithWslEnvironmentAppendsToAnExistingWSLENV()
    {
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x")
            .WithEnvironment("WSLENV", "USERPROFILE/p:TERM")
            .WithEnvironment("AIAKOS_NODE_ID", "wsl-local")
            .WithWslEnvironment();

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal("USERPROFILE/p:TERM:AIAKOS_NODE_ID/u", env["WSLENV"]);
    }

    [Fact]
    public async Task WithWslEnvironmentDoesNotDuplicateAVariableAlreadyInWSLENV()
    {
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x")
            .WithEnvironment("WSLENV", "AIAKOS_HOME/p")
            .WithEnvironment("AIAKOS_HOME", "/home/x")
            .WithWslEnvironment();

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal("AIAKOS_HOME/p", env["WSLENV"]);
    }

    [Fact]
    public async Task WithWslEnvironmentIncludesVariablesAddedAfterIt()
    {
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x")
            .WithWslEnvironment()
            .WithEnvironment("AIAKOS_LATE", "1")
            .WithEnvironment(ctx => ctx.EnvironmentVariables["DOTNET_LATER"] = "2")
            .WithWslOtlpExporter();

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        var entries = TestApp.AddedWslEnvEntries(env["WSLENV"]);
        Assert.Contains("AIAKOS_LATE/u", entries);
        Assert.Contains("DOTNET_LATER/u", entries);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT/u", entries);
        Assert.Contains("OTEL_EXPORTER_OTLP_HEADERS/u", entries);
        Assert.Equal(entries.Length, entries.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task WithWslEnvironmentWithExplicitPrefixesForwardsOnlyThose()
    {
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x")
            .WithEnvironment("FOO_A", "1")
            .WithEnvironment("AIAKOS_B", "2")
            .WithWslEnvironment("FOO_");

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal(["FOO_A/u"], TestApp.AddedWslEnvEntries(env["WSLENV"]));
    }

    [Fact]
    public async Task WithWslEnvironmentCalledTwiceAddsOneWSLENVWithTheUnionOfPrefixes()
    {
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x")
            .WithEnvironment("FOO_A", "1")
            .WithEnvironment("BAR_B", "2")
            .WithEnvironment("AIAKOS_C", "3")
            .WithWslEnvironment("FOO_")
            .WithWslEnvironment("BAR_");

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal(["BAR_B/u", "FOO_A/u"], TestApp.AddedWslEnvEntries(env["WSLENV"]));
        Assert.Single(node.Resource.Annotations.OfType<WslEnvironmentAnnotation>());
    }
}
