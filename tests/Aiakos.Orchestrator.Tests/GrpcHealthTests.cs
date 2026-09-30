using System.Net;

using Aiakos.Orchestrator.Tests.Infrastructure;

using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;

namespace Aiakos.Orchestrator.Tests;

public sealed class GrpcHealthTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CheckReturnsServing()
    {
        await using var factory = new OrchestratorFactory(db.ConnectionString);
        using var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
        });

        var reply = await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), cancellationToken: Ct);

        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, reply.Status);
    }

    [Fact]
    public async Task WithGrpcPortConfiguredGrpcIsOnlyReachableOnThatPort()
    {
        const int grpcPort = 5999;
        await using var factory = new OrchestratorFactory(db.ConnectionString) { GrpcPort = grpcPort };
        var handler = factory.Server.CreateHandler();

        // The test server routes by the Host header, which carries the port of the address.
        using var onGrpcPort = GrpcChannel.ForAddress($"http://localhost:{grpcPort}", new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = false });
        using var onOtherPort = GrpcChannel.ForAddress("http://localhost:5998", new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = false });

        var reply = await new Health.HealthClient(onGrpcPort).CheckAsync(new HealthCheckRequest(), cancellationToken: Ct);
        var error = await Assert.ThrowsAsync<RpcException>(
            async () => await new Health.HealthClient(onOtherPort).CheckAsync(new HealthCheckRequest(), cancellationToken: Ct));

        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, reply.Status);
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
    }

    [Fact]
    public async Task OnKestrelTheGrpcEndpointIsHttp2OnlyAndTheOtherEndpointServesHttp1()
    {
        // Mirrors what Aspire passes (ASPNETCORE_URLS with both endpoints) to check that the
        // per-endpoint protocol selection works on real Kestrel (spec 0001 R13, R14, RK2).
        var grpcPort = OrchestratorFactory.FreePort();
        var httpPort = OrchestratorFactory.FreePort();
        await using var factory = new OrchestratorFactory(db.ConnectionString)
        {
            GrpcPort = grpcPort,
            Urls = [$"http://127.0.0.1:{httpPort}", $"http://127.0.0.1:{grpcPort}"],
        };
        factory.UseKestrel();
        factory.StartServer();

        using var http1 = new HttpClient { DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };

        // The http endpoint answers HTTP/1.1 health probes.
        using var health = await http1.GetAsync(new Uri($"http://127.0.0.1:{httpPort}/health"), Ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpVersion.Version11, health.Version);

        // The gRPC endpoint serves gRPC over h2c.
        using var grpcChannel = GrpcChannel.ForAddress($"http://127.0.0.1:{grpcPort}");
        var reply = await new Health.HealthClient(grpcChannel).CheckAsync(new HealthCheckRequest(), cancellationToken: Ct);
        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, reply.Status);

        // The gRPC endpoint refuses HTTP/1.1 (Kestrel answers 400 on an HTTP/2-only endpoint).
        using var onGrpcPort = await http1.GetAsync(new Uri($"http://127.0.0.1:{grpcPort}/health"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, onGrpcPort.StatusCode);
        Assert.Contains("HTTP/2 only", await onGrpcPort.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // gRPC is not served on the http endpoint.
        using var httpChannel = GrpcChannel.ForAddress($"http://127.0.0.1:{httpPort}");
        var error = await Assert.ThrowsAsync<RpcException>(
            async () => await new Health.HealthClient(httpChannel).CheckAsync(new HealthCheckRequest(), cancellationToken: Ct));
        Assert.NotEqual(StatusCode.OK, error.StatusCode);
    }
}
