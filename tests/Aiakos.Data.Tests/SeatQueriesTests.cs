using System.Text;
using System.Text.Json;

using Aiakos.Core;
using Aiakos.Data.Seats;
using Aiakos.Data.Tests.Infrastructure;

using Npgsql;

namespace Aiakos.Data.Tests;

public sealed class SeatQueriesTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListAsyncReturnsActiveSeatsInRigAndMemberOrder()
    {
        await using var seeded = await CreateSeededAsync();

        var rows = await new SeatQueries(seeded.DataSource).ListAsync(TenantIds.Default, null, Ct);

        Assert.Equal(["drift@demo", "impl@demo", "lead@demo", "review@demo", "stale@demo", "impl@other"],
            rows.Select(static row => row.Address));
    }

    [Fact]
    public async Task ListAsyncFiltersByRigAndTenant()
    {
        await using var seeded = await CreateSeededAsync();
        var queries = new SeatQueries(seeded.DataSource);

        var demoRows = await queries.ListAsync(TenantIds.Default, "demo", Ct);
        var missingRows = await queries.ListAsync(TenantIds.Default, "nope", Ct);
        var otherTenantRows = await queries.ListAsync(seeded.Scenario.OtherTenantId, null, Ct);

        Assert.Equal(5, demoRows.Count);
        Assert.Empty(missingRows);
        var tenantRow = Assert.Single(otherTenantRows);
        Assert.Equal("impl@demo", tenantRow.Address);
    }

    [Fact]
    public async Task ListAsyncReturnsImplStateAndAggregates()
    {
        await using var seeded = await CreateSeededAsync();

        var row = Assert.Single(await new SeatQueries(seeded.DataSource).ListAsync(TenantIds.Default, "demo", Ct),
            static row => row.Address == "impl@demo");

        Assert.Equal("present", row.Session);
        Assert.Equal("working", row.Activity);
        Assert.Equal("tool:Bash", row.ActivityDetail);
        Assert.Equal("resumable", row.Resumability);
        Assert.Equal("ready", row.LaunchOutcome);
        Assert.Equal("resume", row.LaunchDecision);
        Assert.False(row.SpecDrift);
        Assert.Equal("deliver", row.PendingOp);
        Assert.Null(row.LastDeliveryOutcome);
        Assert.Equal(42, row.ContextUsedPercent);
        Assert.Equal("opus", row.Model);
        Assert.Equal(2, row.OpenFindings);
        Assert.Equal("error", row.WorstSeverity);
        Assert.Equal(DateTimeKind.Utc, row.SessionSince!.Value.Kind);
    }

    [Fact]
    public async Task ListAsyncReturnsNeverLaunchedReviewSeatWithNullOptionals()
    {
        await using var seeded = await CreateSeededAsync();

        var row = Assert.Single(await new SeatQueries(seeded.DataSource).ListAsync(TenantIds.Default, "demo", Ct),
            static row => row.Address == "review@demo");

        Assert.Equal("absent", row.Session);
        Assert.Equal("none", row.Activity);
        Assert.Null(row.LaunchId);
        Assert.False(row.SpecDrift);
        Assert.Null(row.ContextUsedPercent);
        Assert.Equal(0, row.OpenFindings);
        Assert.Null(row.WorstSeverity);
    }

    [Fact]
    public async Task ListAsyncReturnsHumanSeatWithNullAgentState()
    {
        await using var seeded = await CreateSeededAsync();

        var row = Assert.Single(await new SeatQueries(seeded.DataSource).ListAsync(TenantIds.Default, "demo", Ct),
            static row => row.Address == "lead@demo");

        Assert.Equal("human", row.Kind);
        Assert.Null(row.Harness);
        Assert.Null(row.Session);
        Assert.False(row.SpecDrift);
        Assert.Equal(0, row.OpenFindings);
    }

    [Fact]
    public async Task ListAsyncDetectsSpecDriftOnlyForActiveSession()
    {
        await using var seeded = await CreateSeededAsync();

        var rows = await new SeatQueries(seeded.DataSource).ListAsync(TenantIds.Default, "demo", Ct);

        Assert.True(Assert.Single(rows, static row => row.Address == "drift@demo").SpecDrift);
        Assert.False(Assert.Single(rows, static row => row.Address == "stale@demo").SpecDrift);
    }

    [Fact]
    public async Task GetDetailAsyncReturnsCurrentLaunchAndOrderedRelatedRows()
    {
        await using var seeded = await CreateSeededAsync();

        var detail = await new SeatQueries(seeded.DataSource).GetDetailAsync(TenantIds.Default, "impl@demo", Ct);

        Assert.NotNull(detail);
        Assert.Equal("impl@demo", detail.Seat.Address);
        Assert.Equal("resume", detail.Launch?.Mode);
        Assert.Equal(20, detail.Transitions.Count);
        Assert.True(detail.Transitions.Zip(detail.Transitions.Skip(1), static (first, second) => first.At >= second.At).All(static ordered => ordered));
        Assert.Equal(["turn-failed", "activity-stale"], detail.Findings.Select(static finding => finding.Kind));
        Assert.Equal(2, detail.Deliveries.Count);
        Assert.True(detail.Deliveries[0].CreatedAt > detail.Deliveries[1].CreatedAt);
    }

    [Fact]
    public async Task GetDetailAsyncReturnsNullForRetiredUnknownOrOtherTenantSeat()
    {
        await using var seeded = await CreateSeededAsync();
        var queries = new SeatQueries(seeded.DataSource);

        Assert.Null(await queries.GetDetailAsync(TenantIds.Default, "gone@demo", Ct));
        Assert.Null(await queries.GetDetailAsync(TenantIds.Default, "nope@demo", Ct));
        Assert.Null(await queries.GetDetailAsync(seeded.Scenario.OtherTenantId, "review@demo", Ct));
    }

    [Fact]
    public async Task GetLaunchAndCommandAsyncScopeSuccessfulAndMissingReadsToTenant()
    {
        await using var seeded = await CreateSeededAsync();
        var queries = new SeatQueries(seeded.DataSource);

        var launch = await queries.GetLaunchAsync(TenantIds.Default, seeded.Scenario.ImplLaunchId, Ct);
        var command = await queries.GetCommandAsync(TenantIds.Default, seeded.Scenario.ImplNewerDeliveryId, Ct);
        var otherTenantLaunch = await queries.GetLaunchAsync(seeded.Scenario.OtherTenantId, seeded.Scenario.ImplLaunchId, Ct);
        var otherTenantCommand = await queries.GetCommandAsync(seeded.Scenario.OtherTenantId, seeded.Scenario.ImplNewerDeliveryId, Ct);

        Assert.Equal("resume", launch?.Mode);
        Assert.Equal(seeded.Scenario.ImplSeatId, launch?.SeatId);
        Assert.Equal("sent", command?.Status);
        Assert.Equal(seeded.Scenario.ImplNewerDeliveryId, command?.CommandId);
        Assert.Null(otherTenantLaunch);
        Assert.Null(otherTenantCommand);
    }

    [Fact]
    public async Task DetailSerializationDoesNotExposeCommandBodyOrLaunchTokenHash()
    {
        await using var seeded = await CreateSeededAsync();

        var detail = await new SeatQueries(seeded.DataSource).GetDetailAsync(TenantIds.Default, "impl@demo", Ct);
        var json = JsonSerializer.Serialize(detail);

        Assert.DoesNotContain(SeatSeed.DeliveryBody, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(SeatSeed.TokenHash)), json, StringComparison.Ordinal);
    }

    private async Task<SeededDatabase> CreateSeededAsync()
    {
        var connectionString = await db.Postgres.CreateDatabaseAsync(Ct);
        var dataSource = NpgsqlDataSource.Create(connectionString);
        await Migrations.CreateMigrator(dataSource).MigrateAsync(Ct);
        var scenario = await SeatSeed.InsertAsync(dataSource, Ct);
        return new SeededDatabase(dataSource, scenario);
    }

    private sealed class SeededDatabase(NpgsqlDataSource dataSource, SeatSeed.Scenario scenario) : IAsyncDisposable
    {
        public NpgsqlDataSource DataSource { get; } = dataSource;

        public SeatSeed.Scenario Scenario { get; } = scenario;

        public ValueTask DisposeAsync() => DataSource.DisposeAsync();
    }
}
