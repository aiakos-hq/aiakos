using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aiakos.Node;

/// <summary>The node's <see cref="ActivitySource"/> and <see cref="Meter"/>, both named <c>Aiakos.Node</c> (spec 0001 R42).</summary>
public sealed class NodeTelemetry
{
    public const string Name = "Aiakos.Node";

    /// <summary>Source of the <c>node.connect</c> activity.</summary>
    public static readonly ActivitySource ActivitySource = new(Name);

    private int _connected;

    public NodeTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        // The factory owns and disposes the meter.
        var meter = meterFactory.Create(Name);
        meter.CreateObservableGauge(
            "aiakos.node.orchestrator.connected",
            () => Volatile.Read(ref _connected),
            description: "1 while the node holds a connection to the orchestrator, otherwise 0.");
        Reconnects = meter.CreateCounter<long>(
            "aiakos.node.orchestrator.reconnects",
            description: "Connections to the orchestrator re-established after the first one.");
    }

    /// <summary>Counter <c>aiakos.node.orchestrator.reconnects</c>.</summary>
    public Counter<long> Reconnects { get; }

    /// <summary>Value of the gauge <c>aiakos.node.orchestrator.connected</c>.</summary>
    public bool Connected
    {
        get => Volatile.Read(ref _connected) == 1;
        set => Volatile.Write(ref _connected, value ? 1 : 0);
    }
}
