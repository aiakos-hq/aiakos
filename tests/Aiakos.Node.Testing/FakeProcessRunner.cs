using System.Collections.ObjectModel;
using Aiakos.Node.Sessions;

namespace Aiakos.Node.Testing;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly object _sync = new();
    private readonly Queue<ProcessResult> _results = new();
    private readonly List<ProcessRequest> _requests = [];

    public IReadOnlyList<ProcessRequest> Requests
    {
        get
        {
            lock (_sync) return _requests.ToArray();
        }
    }

    public void Enqueue(ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_sync) _results.Enqueue(result);
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ct.ThrowIfCancellationRequested();
            if (_results.Count == 0) throw new InvalidOperationException("No scripted process result.");

            var arguments = Array.AsReadOnly(request.Arguments.ToArray());
            var environment = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(request.Environment, StringComparer.Ordinal));
            ReadOnlyMemory<byte>? stdin = request.Stdin is { } bytes
                ? new ReadOnlyMemory<byte>(bytes.ToArray())
                : null;
            _requests.Add(new ProcessRequest(request.FileName, arguments, environment, stdin, request.Timeout));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
