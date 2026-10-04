using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class HarnessReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IHarnessStateProfile Profile = new ClaudeLike();

    [Theory]
    [InlineData(SessionValue.Absent, false, SessionValue.Absent, ActivityValue.None)]
    [InlineData(SessionValue.Starting, false, SessionValue.Present, ActivityValue.Idle)]
    [InlineData(SessionValue.Present, false, SessionValue.Present, ActivityValue.Idle)]
    [InlineData(SessionValue.Exited, false, SessionValue.Unknown, ActivityValue.Unknown)]
    [InlineData(SessionValue.Unknown, false, SessionValue.Present, ActivityValue.Idle)]
    [InlineData(SessionValue.Starting, true, SessionValue.Present, ActivityValue.Idle)]
    [InlineData(SessionValue.Unknown, true, SessionValue.Present, ActivityValue.Idle)]
    public void S11ReadinessAppliesOnlyToMatchingNativeSession(SessionValue initial, bool readinessSeen,
        SessionValue expectedSession, ActivityValue expectedActivity)
    {
        var state = State(initial) with { ReadinessSeen = readinessSeen };
        var step = Apply(state, Harness(HarnessEventKind.SessionStarted, "native-1", Attrs(("source", "startup"))));

        Assert.Equal(expectedSession, step.State.KnownSession);
        Assert.Equal(expectedActivity, step.State.KnownActivity);
        Assert.True(step.State.ReadinessSeen);
        Assert.Equal(Now, step.State.LastEventAt);
        if (initial == SessionValue.Exited)
            Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingSourcesDisagree && finding.Open);
        if (expectedSession == SessionValue.Present)
            Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingLaunchUnconfirmed && !finding.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Absent, false, SessionValue.Absent, EventDisposition.Orphan)]
    [InlineData(SessionValue.Starting, true, SessionValue.Starting, EventDisposition.Evidence)]
    [InlineData(SessionValue.Present, true, SessionValue.Exited, EventDisposition.Applied)]
    [InlineData(SessionValue.Exited, true, SessionValue.Exited, EventDisposition.Applied)]
    [InlineData(SessionValue.Unknown, true, SessionValue.Exited, EventDisposition.Applied)]
    public void S12SessionEndedAfterReadinessExitsKnownSessions(SessionValue initial, bool readinessSeen,
        SessionValue expected, EventDisposition disposition)
    {
        var step = Apply(State(initial) with { ReadinessSeen = readinessSeen },
            Harness(HarnessEventKind.SessionEnded, "native-1", Attrs()));

        Assert.Equal(expected, step.State.KnownSession);
        Assert.Equal(disposition, step.Disposition);
        if (expected == SessionValue.Exited)
            Assert.Equal(ActivityValue.None, step.State.KnownActivity);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Absent)]
    [InlineData(SessionValue.Starting)]
    [InlineData(SessionValue.Present)]
    [InlineData(SessionValue.Exited)]
    [InlineData(SessionValue.Unknown)]
    public void S13SessionEndedWithoutReadinessIsOrphan(SessionValue initial)
    {
        var state = State(initial) with { ReadinessSeen = false };
        var step = Apply(state, Harness(HarnessEventKind.SessionEnded, "native-1", Attrs()));

        Assert.Equal(EventDisposition.Orphan, step.Disposition);
        Assert.Equal(state.KnownSession, step.State.KnownSession);
        Assert.Equal(state.KnownActivity, step.State.KnownActivity);
        Assert.Equal(Now, step.State.LastEventAt);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingActivityStale && !finding.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(HarnessEventKind.Telemetry)]
    [InlineData(HarnessEventKind.Other)]
    [InlineData(HarnessEventKind.Unspecified)]
    [InlineData((HarnessEventKind)99)]
    public void A14TelemetryAndOtherKindsSetLastEventAndResolveStaleActivity(HarnessEventKind kind)
    {
        var state = State(SessionValue.Present) with
        {
            KnownActivity = ActivityValue.Working,
            Activity = ActivityValue.Working,
        };
        var step = Apply(state, Harness(kind, "native-1", Attrs()));

        Assert.Equal(Now, step.State.LastEventAt);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingActivityStale && !finding.Open);
        Assert.Equal(state.KnownActivity, step.State.KnownActivity);
        Assert.Equal(EventDisposition.Applied, step.Disposition);
        SeatAssert.Invariants(step);
    }

    private static SeatStep Apply(SeatState state, SeatInput input) =>
        SeatStateMachine.Apply(state, input, Profile, Now);

    internal static SeatState State(SessionValue session) => SeatState.Initial(Now) with
    {
        Session = session,
        KnownSession = session,
        SessionReason = SessionReason(session),
        KnownSessionReason = SessionReason(session),
        Activity = Activity(session),
        KnownActivity = Activity(session),
        ActivityReason = ActivityReason(session),
        KnownActivityReason = ActivityReason(session),
        Resumability = ResumabilityValue.Resumable,
        Desired = SeatDesired.Up,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NativeSessionId = "native-1",
        NodeInstanceId = NodeId,
    };

    internal static EventReceived Harness(HarnessEventKind kind, string nativeSessionId,
        IReadOnlyDictionary<string, string> attributes) =>
        new(NodeId, 1, 0, LaunchId, new HarnessBody(kind, nativeSessionId, attributes));

    internal static IReadOnlyDictionary<string, string> Attrs(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static ActivityValue Activity(SessionValue session) => session switch
    {
        SessionValue.Absent or SessionValue.Exited => ActivityValue.None,
        SessionValue.Starting or SessionValue.Unknown => ActivityValue.Unknown,
        _ => ActivityValue.Idle,
    };

    private static string? ActivityReason(SessionValue session) => session switch
    {
        SessionValue.Starting => SeatVocabulary.ActivityReasonNotReady,
        SessionValue.Unknown => SeatVocabulary.ActivityReasonSessionUnknown,
        _ => null,
    };

    private static string? SessionReason(SessionValue session) => session == SessionValue.Unknown
        ? SeatVocabulary.SessionReasonSourcesDisagree
        : null;
}
