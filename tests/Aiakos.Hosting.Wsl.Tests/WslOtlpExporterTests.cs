using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Aiakos.Hosting.Wsl.Tests;

public sealed class WslOtlpExporterTests
{
    [Fact]
    public async Task WithWslOtlpExporterRewritesTheDashboardEndpointReferenceTo127001()
    {
        var builder = TestApp.CreateBuilder();
        AddAllocatedDashboard(builder, "localhost", 19180);
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x").WithWslOtlpExporter();
        using var app = builder.Build();

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal("http://127.0.0.1:19180", env["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var entries = TestApp.AddedWslEnvEntries(env["WSLENV"]);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT/u", entries);
        Assert.Contains("OTEL_EXPORTER_OTLP_HEADERS/u", entries);
        Assert.Contains("OTEL_SERVICE_NAME/u", entries);
    }

    [Fact]
    public async Task WithOtlpExporterInjectsAnEndpointReferenceInAspire135()
    {
        // Pins the Aspire behaviour the rewrite depends on (spike 0003 pitfall 2, spec 0001 RK3).
        var builder = TestApp.CreateBuilder();
        AddAllocatedDashboard(builder, "localhost", 19180);
        var plain = builder.AddExecutable("plain", "x", ".").WithOtlpExporter();
        using var app = builder.Build();

        var context = new EnvironmentCallbackContext(
            builder.ExecutionContext, plain.Resource, [], TestContext.Current.CancellationToken);
        foreach (var callback in plain.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        var endpoint = Assert.IsType<EndpointReference>(context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("http://localhost:19180", endpoint.Url);
    }

    [Fact]
    public async Task WithWslOtlpExporterRewritesTheHostUrlFallbackAndKeepsSchemeAndPort()
    {
        // Without a dashboard resource in the model, Aspire injects a HostUrl from configuration.
        var builder = TestApp.CreateBuilder();
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x").WithWslOtlpExporter();

        var env = await TestApp.EnvironmentAsync(builder, node.Resource);

        Assert.Equal("http://127.0.0.1:19180", env["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }

    private static void AddAllocatedDashboard(IDistributedApplicationBuilder builder, string address, int port)
    {
        var dashboard = builder.AddExecutable("aspire-dashboard", "dashboard", ".")
            .WithEndpoint(name: "otlp-grpc", scheme: "http", port: port, isProxied: false);
        var endpoint = dashboard.Resource.Annotations.OfType<EndpointAnnotation>().Single();
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, address, port);
    }
}
