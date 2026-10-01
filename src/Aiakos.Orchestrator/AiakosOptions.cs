namespace Aiakos.Orchestrator;

/// <summary>The orchestrator's view of the <c>Aiakos</c> configuration section.</summary>
public sealed class AiakosOptions
{
    /// <summary>Configuration section.</summary>
    public const string Section = "Aiakos";

    /// <summary>Instance name (telemetry attribute <c>aiakos.instance</c>).</summary>
    public string? Instance { get; set; }

    /// <summary>Orchestrator endpoint settings (<c>Aiakos:Orchestrator</c>).</summary>
    public OrchestratorEndpointOptions Orchestrator { get; set; } = new();

    /// <summary>
    /// Nodes allowed to connect (<c>Aiakos:Nodes:N:Id</c>, <c>Aiakos:Nodes:N:Token</c>). Bound
    /// only; token validation belongs to spec 0002.
    /// </summary>
    public IList<NodeRegistration> Nodes { get; } = [];
}

/// <summary>Orchestrator endpoint settings.</summary>
public sealed class OrchestratorEndpointOptions
{
    /// <summary>Configuration key of <see cref="GrpcPort"/>.</summary>
    public const string GrpcPortKey = "Aiakos:Orchestrator:GrpcPort";

    /// <summary>
    /// Port of the gRPC endpoint (h2c, HTTP/2 only; spec 0001 R13). When unset, no endpoint is
    /// switched to HTTP/2 only and gRPC services are reachable on every endpoint (tests).
    /// </summary>
    public int? GrpcPort { get; set; }
}

/// <summary>A node allowed to connect. Deliberately not a record, so the token never appears in <c>ToString()</c>.</summary>
public sealed class NodeRegistration
{
    /// <summary>Node id (<c>AIAKOS_NODE_ID</c> on the node).</summary>
    public string Id { get; set; } = "";

    /// <summary>Shared secret of the node (<c>AIAKOS_NODE_TOKEN</c> on the node).</summary>
    public string Token { get; set; } = "";
}
