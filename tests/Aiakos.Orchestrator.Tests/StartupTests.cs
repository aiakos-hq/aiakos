using System.Net;

using Aiakos.Core;
using Aiakos.Data;
using Aiakos.Orchestrator.Tests.Infrastructure;

using Akka.Actor;

using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.Orchestrator.Tests;

public sealed class StartupTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HealthIsHealthyAfterStartupAndDatabaseIsMigrated()
    {
        await using var factory = new OrchestratorFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);
        using var alive = await client.GetAsync(new Uri("/alive", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);

        var tenant = await factory.Services.GetRequiredService<TenantRepository>().GetAsync(TenantIds.Default, Ct);
        Assert.NotNull(tenant);
    }

    [Fact]
    public async Task ActorSystemRunsAfterStartAndTerminatesAfterStop()
    {
        var factory = new OrchestratorFactory(db.ConnectionString);
        ActorSystem system;
        try
        {
            factory.StartServer();
            system = factory.Services.GetRequiredService<ActorSystem>();

            Assert.Equal("aiakos", system.Name);
            Assert.False(system.WhenTerminated.IsCompleted);
        }
        finally
        {
            await factory.DisposeAsync();
        }

        await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.True(system.WhenTerminated.IsCompletedSuccessfully);
    }
}
