namespace Aiakos.Core;

/// <summary>
/// Defaults that identify an Aiakos instance: name, homes, port base and database (spec 0001 R46,
/// spec 0007 R6).
/// The dev AppHost refuses the released values (R18), and the CLI (#15) reads the same constants,
/// so the dev stack and the released tool never share a home, a port or a database (ADR 0008).
/// </summary>
public static class InstanceDefaults
{
    /// <summary>Instance name reserved for the released tool.</summary>
    public const string ReleasedInstance = "release";

    /// <summary>WSL home of the released tool, relative to the WSL user's home.</summary>
    public const string ReleasedWslHome = ".aiakos";

    /// <summary>Windows home of the released tool, relative to <c>%USERPROFILE%</c>.</summary>
    public const string ReleasedWindowsHome = ".aiakos";

    /// <summary>Port base of the released tool (gRPC at +0, API at +1, Postgres at +2, hooks at +10).</summary>
    public const int ReleasedPortBase = 7180;

    /// <summary>Postgres container of the released tool in container mode.</summary>
    public const string ReleasedPostgresContainer = "aiakos-release-postgres";

    /// <summary>Postgres data volume of the released tool in container mode.</summary>
    public const string ReleasedPostgresVolume = "aiakos-release-pgdata";

    /// <summary>Instance name of the dev AppHost.</summary>
    public const string DevInstance = "dev";

    /// <summary>WSL home of the dev AppHost, relative to the WSL user's home.</summary>
    public const string DevWslHome = ".aiakos-dev";

    /// <summary>Windows home of the dev AppHost, relative to <c>%USERPROFILE%</c> (connection file, API token).</summary>
    public const string DevWindowsHome = ".aiakos-dev";

    /// <summary>Port base of the dev AppHost.</summary>
    public const int DevPortBase = 5180;

    /// <summary>Offset from the port base of the orchestrator's gRPC endpoint.</summary>
    public const int GrpcPortOffset = 0;

    /// <summary>Offset from the port base of the orchestrator's HTTP endpoint: <c>/health</c>, <c>/alive</c> and the local API <c>/v1</c> (ADR 0035).</summary>
    public const int ApiPortOffset = 1;

    /// <summary>Offset from the port base of the released tool's Postgres in container mode.</summary>
    public const int PostgresPortOffset = 2;

    /// <summary>Offset from the port base of the node's hook ingest (spec 0005).</summary>
    public const int HookIngestPortOffset = 10;

    /// <summary>File in the Windows home that names a running instance's API (spec 0007 R20).</summary>
    public const string ConnectionFileName = "connection.json";

    /// <summary>API token file, relative to the Windows home (spec 0007, Instance files).</summary>
    public static readonly string ApiTokenFile = Path.Combine("secrets", "api-token");
}
