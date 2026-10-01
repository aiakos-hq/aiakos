using Aiakos.Core;
using Aiakos.Data.Tests.Infrastructure;

namespace Aiakos.Data.Tests;

public sealed class TenantRepositoryTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Migrations.CreateMigrator(db.DataSource).MigrateAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAsyncReturnsDefaultTenant()
    {
        var tenant = await new TenantRepository(db.DataSource).GetAsync(TenantIds.Default, Ct);

        Assert.NotNull(tenant);
        Assert.Equal(TenantIds.Default, tenant.TenantId);
        Assert.Equal(TenantIds.DefaultSlug, tenant.Slug);
        Assert.Equal("Default tenant", tenant.Name);
        Assert.Equal(DateTimeKind.Utc, tenant.CreatedAt.Kind);
        Assert.InRange(tenant.CreatedAt, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task GetAsyncReturnsNullForUnknownTenant()
    {
        var tenant = await new TenantRepository(db.DataSource).GetAsync(Guid.CreateVersion7(), Ct);

        Assert.Null(tenant);
    }
}
