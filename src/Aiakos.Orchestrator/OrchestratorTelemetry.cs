using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aiakos.Orchestrator;

/// <summary>The orchestrator's <see cref="ActivitySource"/> and <see cref="Meter"/> (spec 0001 R42).</summary>
internal static class OrchestratorTelemetry
{
    /// <summary>Name of the activity source and the meter.</summary>
    public const string Name = "Aiakos.Orchestrator";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static readonly Meter Meter = new(Name);

    /// <summary>Migration scripts applied at startup.</summary>
    public static readonly Counter<long> MigrationsApplied =
        Meter.CreateCounter<long>("aiakos.orchestrator.migrations.applied", unit: "{script}", description: "Migration scripts applied at startup.");
}
