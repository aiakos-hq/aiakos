using Aiakos.Node.Sessions;

namespace Aiakos.Node.Testing;

public interface ISessionHostRig : IAsyncDisposable
{
    ISessionHost Host { get; }
    SessionSpec NewSpec(string seat = "impl", bool ignoresGracefulStop = false);
    string MissingDirectory { get; }
    Task<byte[]> ReceivedAsync(SessionHandle session);
    Task PrintAsync(SessionHandle session, IReadOnlyList<string> lines);
    Task ExitPaneAsync(SessionHandle session, int exitCode);
}
