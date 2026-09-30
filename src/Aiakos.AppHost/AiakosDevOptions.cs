namespace Aiakos.AppHost;

/// <summary>
/// The <c>Aiakos</c> configuration section of the dev AppHost (spec 0001 R11, Design →
/// Configuration). Defaults live in <c>appsettings.json</c>; user secrets, environment variables
/// and the command line override them.
/// </summary>
public sealed class AiakosDevOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Aiakos";

    /// <summary><c>Aiakos:Instance</c>: telemetry attribute and Postgres volume name.</summary>
    public string Instance { get; set; } = string.Empty;

    /// <summary><c>Aiakos:PortBase</c>: orchestrator gRPC at +0, HTTP/API at +1, node hook ingest at +10.</summary>
    public int PortBase { get; set; }

    /// <summary>
    /// <c>Aiakos:WindowsHome</c>: relative to <c>%USERPROFILE%</c>; holds <c>connection.json</c> and the
    /// API token file for the development CLI (spec 0007 R29).
    /// </summary>
    public string WindowsHome { get; set; } = string.Empty;

    /// <summary><c>Aiakos:Wsl</c>: where the node runs.</summary>
    public WslOptions Wsl { get; set; } = new();

    /// <summary>The orchestrator's gRPC port.</summary>
    public int GrpcPort => PortBase + Core.InstanceDefaults.GrpcPortOffset;

    /// <summary>The orchestrator's HTTP port: <c>/health</c>, <c>/alive</c> and the local API (R14).</summary>
    public int ApiPort => PortBase + Core.InstanceDefaults.ApiPortOffset;

    /// <summary>The node's hook-ingest port (spec 0005).</summary>
    public int HookIngestPort => PortBase + Core.InstanceDefaults.HookIngestPortOffset;

    /// <summary>The <c>Aiakos:Wsl</c> section.</summary>
    public sealed class WslOptions
    {
        /// <summary><c>Aiakos:Wsl:Distro</c>: the distro for <c>wsl.exe -d</c>.</summary>
        public string Distro { get; set; } = string.Empty;

        /// <summary><c>Aiakos:Wsl:Home</c>: <c>AIAKOS_HOME</c>, relative to the WSL user's home.</summary>
        public string Home { get; set; } = string.Empty;

        /// <summary><c>Aiakos:Wsl:NodeId</c>: <c>AIAKOS_NODE_ID</c>.</summary>
        public string NodeId { get; set; } = string.Empty;
    }
}
