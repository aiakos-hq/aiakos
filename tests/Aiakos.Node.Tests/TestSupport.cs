using System.Collections.Concurrent;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aiakos.Node.Tests;

/// <summary>Collects formatted log lines from every logger.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public string Text => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception));
    }
}

/// <summary>A scratch directory removed on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() =>
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aiakos-node-tests", Guid.NewGuid().ToString("N"));

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}

internal static class NodeHost
{
    /// <summary>A <see cref="NodeProgram.RunAsync"/> hook that overrides the settings and captures logs.</summary>
    public static Action<HostApplicationBuilder> With(IDictionary<string, string?> settings, ILoggerProvider logs) =>
        builder =>
        {
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
        };

    public static Dictionary<string, string?> ValidSettings(string home) => new()
    {
        [NodeOptions.OrchestratorUrlVariable] = "http://127.0.0.1:1",
        [NodeOptions.HomeVariable] = home,
        [NodeOptions.NodeIdVariable] = "test-node",
        [NodeOptions.NodeTokenVariable] = "token",
    };
}
