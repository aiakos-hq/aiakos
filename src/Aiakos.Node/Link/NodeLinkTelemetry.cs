using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aiakos.Contracts.Node.V1;

[assembly: InternalsVisibleTo("Aiakos.Node.Tests")]

namespace Aiakos.Node.Link;

public static class NodeLinkTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Aiakos.Node.Link");

    internal static IDisposable? StartReceiveActivity(TraceContext? trace)
    {
        var parent = default(ActivityContext);
        var hasParent = trace is not null && ActivityContext.TryParse(trace.Traceparent, trace.Tracestate, isRemote: true, out parent);
        var ambient = Activity.Current;
        if (!hasParent) Activity.Current = null;
        try
        {
            var activity = ActivitySource.StartActivity("node-link.receive", ActivityKind.Consumer, parent);
            if (activity is null)
            {
                Activity.Current = ambient;
                return null;
            }
            return new ReceiveActivityScope(activity, ambient);
        }
        catch
        {
            Activity.Current = ambient;
            throw;
        }
    }

    internal static void SetTrace(ConnectRequest envelope)
    {
        var current = Activity.Current;
        envelope.Trace = current?.IdFormat == ActivityIdFormat.W3C
            ? new TraceContext { Traceparent = current.Id ?? string.Empty, Tracestate = current.TraceStateString ?? string.Empty }
            : new TraceContext();
    }

    private sealed class ReceiveActivityScope(Activity activity, Activity? ambient) : IDisposable
    {
        private Activity? _activity = activity;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _activity, null);
            try { current?.Dispose(); }
            finally { Activity.Current = ambient; }
        }
    }
}
