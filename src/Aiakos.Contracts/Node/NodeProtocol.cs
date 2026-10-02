using Aiakos.Contracts.Node.V1;

namespace Aiakos.Contracts.Node;

public static class NodeProtocol
{
    public const uint Major = 1;
    public const uint Minor = 0;

    public static ProtocolVersion Current => new() { Major = Major, Minor = Minor };

    public static ProtocolVersion? Negotiate(ProtocolVersion? node, ProtocolVersion? orchestrator)
    {
        if (node is null || orchestrator is null || node.Major != orchestrator.Major)
        {
            return null;
        }

        return new ProtocolVersion { Major = node.Major, Minor = Math.Min(node.Minor, orchestrator.Minor) };
    }
}
