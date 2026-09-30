namespace Aiakos.Core;

/// <summary>
/// Defaults that identify an Aiakos instance: name, WSL home and port base (spec 0001 R46).
/// The dev AppHost refuses the released values (R18), and the CLI (#15) reads the same constants,
/// so the dev stack and the released tool never share a home, a port or a database (ADR 0008).
/// </summary>
public static class InstanceDefaults
{
    /// <summary>Instance name reserved for the released tool.</summary>
    public const string ReleasedInstance = "release";

    /// <summary>WSL home of the released tool, relative to the WSL user's home.</summary>
    public const string ReleasedWslHome = ".aiakos";

    /// <summary>Port base of the released tool (gRPC at +0, node hook ingest at +10).</summary>
    public const int ReleasedPortBase = 7180;

    /// <summary>Instance name of the dev AppHost.</summary>
    public const string DevInstance = "dev";

    /// <summary>WSL home of the dev AppHost, relative to the WSL user's home.</summary>
    public const string DevWslHome = ".aiakos-dev";

    /// <summary>Port base of the dev AppHost.</summary>
    public const int DevPortBase = 5180;

    /// <summary>Offset from the port base of the orchestrator's gRPC endpoint.</summary>
    public const int GrpcPortOffset = 0;

    /// <summary>Offset from the port base of the node's hook ingest (spec 0005).</summary>
    public const int HookIngestPortOffset = 10;
}
