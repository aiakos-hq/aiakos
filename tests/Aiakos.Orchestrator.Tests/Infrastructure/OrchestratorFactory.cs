using System.Globalization;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.Orchestrator.Tests.Infrastructure;

/// <summary>Hosts the orchestrator against a test database.</summary>
public sealed class OrchestratorFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>Value for <c>Aiakos:Orchestrator:GrpcPort</c>; unset by default.</summary>
    public int? GrpcPort { get; init; }

    /// <summary>URLs Kestrel listens on (only with <c>UseKestrel()</c>).</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>Extra service registrations, applied after the orchestrator's own.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:aiakos", connectionString);
        builder.UseSetting("Aiakos:Instance", "test");
        builder.UseSetting("Aiakos:Nodes:0:Id", "test-node");
        builder.UseSetting("Aiakos:Nodes:0:Token", "test-token");
        if (GrpcPort is { } port)
        {
            builder.UseSetting("Aiakos:Orchestrator:GrpcPort", port.ToString(CultureInfo.InvariantCulture));
        }

        if (Urls.Count > 0)
        {
            builder.UseUrls([.. Urls]);
        }

        if (ConfigureServices is { } configure)
        {
            builder.ConfigureTestServices(configure);
        }
    }

    /// <summary>Returns a currently free loopback TCP port.</summary>
    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
