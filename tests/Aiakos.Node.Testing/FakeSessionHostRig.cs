using Aiakos.Node.Sessions;

namespace Aiakos.Node.Testing;

public sealed class FakeSessionHostRig : ISessionHostRig
{
    private readonly FakeSessionHost _host;

    public FakeSessionHostRig(TimeProvider? time = null)
    {
        _host = new FakeSessionHost(time);
        _host.Files.Remove(MissingDirectory);
    }

    public ISessionHost Host => _host;

    public string MissingDirectory => "/missing";

    public SessionSpec NewSpec(string seat = "impl", bool ignoresGracefulStop = false)
    {
        var spec = new SessionSpec(
            $"seat-{seat}",
            $"{seat}@demo",
            Guid.NewGuid().ToString("N"),
            "fake",
            ["/bin/fake"],
            "/work",
            "/home/seat",
            new Dictionary<string, string>(),
            TerminalSize.Default,
            null,
            new Dictionary<string, string>());

        if (ignoresGracefulStop)
        {
            _host.RegisterIgnoresGracefulStop(spec);
        }

        return spec;
    }

    public Task<byte[]> ReceivedAsync(SessionHandle session) => Task.FromResult(_host.Received(session));

    public Task PrintAsync(SessionHandle session, IReadOnlyList<string> lines)
    {
        _host.Print(session, lines);
        return Task.CompletedTask;
    }

    public Task ExitPaneAsync(SessionHandle session, int exitCode)
    {
        _host.Exit(session, exitCode, null);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
