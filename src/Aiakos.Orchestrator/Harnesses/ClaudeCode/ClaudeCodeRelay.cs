using Aiakos.Contracts.Node.V1;
using Google.Protobuf;

namespace Aiakos.Orchestrator.Harnesses.ClaudeCode;

public static class ClaudeCodeRelay
{
    public static SeatFile BuildFile()
    {
        using var stream = typeof(ClaudeCodeRelay).Assembly.GetManifestResourceStream("Aiakos.ClaudeCode.HookRelay")
            ?? throw new InvalidOperationException("Embedded Claude Code hook relay is unavailable.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return new SeatFile
        {
            Root = FileRoot.SeatHome,
            Path = "aiakos/bin/aiakos-hook-relay",
            Mode = 493,
            Expand = false,
            Content = ByteString.CopyFrom(content.ToArray())
        };
    }
}
