using System.Collections.Concurrent;
using System.Diagnostics;
using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Link;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeLinkTelemetryTests
{
    [Fact]
    public void ReceiveActivitiesUseRemoteParentsAndInvalidParentsStartRoots()
    {
        var observed = new ConcurrentBag<(string? TraceId, string? ParentSpanId, ActivityKind Kind, string Name)>();
        using var listener = Listen(NodeLinkTelemetry.ActivitySource, observed);
        using var ambient = new Activity("unrelated-connect").SetIdFormat(ActivityIdFormat.W3C).Start();
        var firstParent = Context("11111111111111111111111111111111", "1111111111111111");
        var secondParent = Context("22222222222222222222222222222222", "2222222222222222");

        using (NodeLinkTelemetry.StartReceiveActivity(ToTrace(firstParent)))
        {
            Assert.Equal(firstParent.TraceId, Activity.Current!.TraceId);
            Assert.Equal(firstParent.SpanId, Activity.Current.ParentSpanId);
        }
        using (NodeLinkTelemetry.StartReceiveActivity(ToTrace(secondParent)))
        {
            Assert.Equal(secondParent.TraceId, Activity.Current!.TraceId);
            Assert.Equal(secondParent.SpanId, Activity.Current.ParentSpanId);
        }
        using (NodeLinkTelemetry.StartReceiveActivity(new TraceContext { Traceparent = "invalid" }))
        {
            Assert.NotEqual(ambient.TraceId, Activity.Current!.TraceId);
            Assert.Equal(default, Activity.Current.ParentSpanId);
        }
        using (NodeLinkTelemetry.StartReceiveActivity(null))
        {
            Assert.NotEqual(ambient.TraceId, Activity.Current!.TraceId);
            Assert.Equal(default, Activity.Current.ParentSpanId);
        }

        Assert.Equal(4, observed.Count);
        Assert.All(observed, item =>
        {
            Assert.Equal("node-link.receive", item.Name);
            Assert.Equal(ActivityKind.Consumer, item.Kind);
        });
        Assert.Contains(observed, item => item.TraceId == firstParent.TraceId.ToString() && item.ParentSpanId == firstParent.SpanId.ToString());
        Assert.Contains(observed, item => item.TraceId == secondParent.TraceId.ToString() && item.ParentSpanId == secondParent.SpanId.ToString());
        Assert.Contains(observed, item => item.ParentSpanId == default(ActivitySpanId).ToString());
    }

    [Fact]
    public void SenderUsesAmbientW3CContextAndClearsOtherFormats()
    {
        var request = new ConnectResponse();
        using (var activity = new Activity("outgoing").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            NodeLinkTelemetry.SetTrace(request);
            Assert.Equal(activity.Id, request.Trace.Traceparent);
            Assert.Equal(activity.TraceStateString ?? string.Empty, request.Trace.Tracestate);
        }

        using (var activity = new Activity("hierarchical").SetIdFormat(ActivityIdFormat.Hierarchical).Start())
        {
            NodeLinkTelemetry.SetTrace(request);
            Assert.Equal(string.Empty, request.Trace.Traceparent);
            Assert.Equal(string.Empty, request.Trace.Tracestate);
        }
    }

    private static ActivityListener Listen(ActivitySource source,
        ConcurrentBag<(string? TraceId, string? ParentSpanId, ActivityKind Kind, string Name)> observed)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Add((activity.TraceId.ToString(), activity.ParentSpanId.ToString(), activity.Kind, activity.OperationName)),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static ActivityContext Context(string traceId, string spanId) => new(
        ActivityTraceId.CreateFromString(traceId), ActivitySpanId.CreateFromString(spanId), ActivityTraceFlags.Recorded,
        traceState: "vendor=value", isRemote: true);

    private static TraceContext ToTrace(ActivityContext context) => new()
    {
        Traceparent = $"00-{context.TraceId}-{context.SpanId}-01",
        Tracestate = context.TraceState ?? string.Empty,
    };
}
