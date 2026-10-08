using Aiakos.Cli.Instances;

namespace Aiakos.Cli.Tests.Instances;

public sealed class InstanceLayoutTests
{
    [Fact]
    public void ResolvesReleaseAndNamedInstancePaths()
    {
        Assert.Equal(new InstancePaths("/owner/.aiakos", ".aiakos", "aiakos-release-postgres", "aiakos-release-pgdata"),
            InstanceLayout.Resolve("/owner", "release"));
        Assert.Equal(new InstancePaths("/owner/.aiakos-demo", ".aiakos-demo", "aiakos-demo-postgres", "aiakos-demo-pgdata"),
            InstanceLayout.Resolve("/owner", "demo"));
        var longInstance = new string('a', 63);
        Assert.Equal($".aiakos-{longInstance}", InstanceLayout.Resolve("/owner", longInstance).WslHome);
    }

    [Theory]
    [InlineData("BAD")]
    [InlineData("-bad")]
    [InlineData("bad_")]
    [InlineData("bad/name")]
    [InlineData("a123456789012345678901234567890123456789012345678901234567890123")]
    public void RejectsInvalidInstanceSlugs(string instance)
    {
        var exception = Assert.Throws<ArgumentException>(() => InstanceLayout.Resolve("/owner", instance));
        Assert.Equal("Invalid instance name.", exception.Message);
    }

    [Fact]
    public void SerializesTheConfigurationGoldenWithNullEndpointRetained()
    {
        var configuration = BaseConfiguration();

        Assert.Equal(
            "{\"instance\":\"release\",\"port_base\":7180,\"operator\":\"owner\",\"wsl\":{\"distro\":\"Ubuntu\",\"home\":\".aiakos\",\"node_id\":\"wsl-local\"},\"database\":{\"mode\":\"container\",\"image\":\"postgres:18\"},\"telemetry\":{\"dashboard\":false,\"otlp_endpoint\":null}}\n",
            InstanceConfigurationJson.Serialize(configuration));
    }

    [Fact]
    public void DeserializesTheConfigurationGoldenAndRejectsMalformedConfiguration()
    {
        const string json = "{\"instance\":\"release\",\"port_base\":7180,\"operator\":\"owner\",\"wsl\":{\"distro\":\"Ubuntu\",\"home\":\".aiakos\",\"node_id\":\"wsl-local\"},\"database\":{\"mode\":\"container\",\"image\":\"postgres:18\"},\"telemetry\":{\"dashboard\":false,\"otlp_endpoint\":null}}";

        Assert.Equal(BaseConfiguration(), InstanceConfigurationJson.Deserialize(json));
        var exception = Assert.Throws<FormatException>(() => InstanceConfigurationJson.Deserialize("{}"));
        Assert.Equal("Invalid instance configuration.", exception.Message);
    }

    [Fact]
    public void ValidatesReservedDevAndPortOverlapRules()
    {
        var initialized = new[] { BaseConfiguration() };
        Assert.Equal("Instance ports overlap an initialized instance.",
            InstanceLayout.Validate(NamedConfiguration("demo", 7181), initialized));
        Assert.Equal("Instance ports overlap an initialized instance.",
            InstanceLayout.Validate(NamedConfiguration("demo", 5170), []));
        Assert.Equal("The dev instance is reserved for the AppHost.",
            InstanceLayout.Validate(NamedConfiguration("dev", 5180), []));
    }

    [Fact]
    public void ValidatesDatabaseAndTelemetryConfiguration()
    {
        Assert.Null(InstanceLayout.Validate(BaseConfiguration(), []));
        Assert.Null(InstanceLayout.Validate(BaseConfiguration() with
        {
            Database = new DatabaseConfiguration("external", null, "C:\\secrets\\db")
        }, []));
        Assert.Equal("Invalid instance configuration.", InstanceLayout.Validate(BaseConfiguration() with
        {
            Database = new DatabaseConfiguration("external", null, "/secrets/db")
        }, []));
        Assert.Equal("Invalid instance configuration.", InstanceLayout.Validate(BaseConfiguration() with
        {
            Telemetry = new TelemetryConfiguration(true, "http://localhost:4318")
        }, []));
        Assert.Equal("Invalid instance configuration.", InstanceLayout.Validate(BaseConfiguration() with
        {
            PortBase = 0
        }, []));
        Assert.Equal("Invalid instance configuration.", InstanceLayout.Validate(BaseConfiguration() with
        {
            Wsl = new WslConfiguration("Ubuntu", ".aiakos-other", "wsl-local")
        }, []));
    }

    [Fact]
    public void ValidationDoesNotMutateConfigurationOrInitializedSnapshots()
    {
        var initialized = new List<InstanceConfiguration> { BaseConfiguration() };
        var candidate = NamedConfiguration("other", 9000);
        var original = candidate with { };

        _ = InstanceLayout.Validate(candidate, initialized);

        Assert.Equal(original, candidate);
        Assert.Single(initialized);
        Assert.Equal(BaseConfiguration(), initialized[0]);
    }

    private static InstanceConfiguration BaseConfiguration() => new("release", 7180, "owner",
        new WslConfiguration("Ubuntu", ".aiakos", "wsl-local"),
        new DatabaseConfiguration("container", "postgres:18", null),
        new TelemetryConfiguration(false, null));

    private static InstanceConfiguration NamedConfiguration(string instance, int portBase) => new(instance, portBase,
        "owner", new WslConfiguration("Ubuntu", $".aiakos-{instance}", "wsl-local"),
        new DatabaseConfiguration("container", "postgres:18", null), new TelemetryConfiguration(false, null));
}
