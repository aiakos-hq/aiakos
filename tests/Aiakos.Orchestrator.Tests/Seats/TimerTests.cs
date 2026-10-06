using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class TimerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void T9QuietTimeoutRequiresPresentWorkingAndElapsedProfileTimeout()
    {
        var eligible = State(SessionValue.Present, ActivityValue.Working) with
        {
            LastEventAt = Now.AddMinutes(-10)
        };
        var timedOut = Apply(eligible, new QuietTimeoutFired());

        Assert.Equal(ActivityValue.Unknown, timedOut.State.KnownActivity);
        Assert.Equal(SeatVocabulary.ActivityReasonQuietTimeout, timedOut.State.KnownActivityReason);
        Assert.Equal(Now, timedOut.State.ActivitySince);
        Assert.Contains(timedOut.Transitions,
            transition => transition.Axis == "activity" && transition.Rule == "A15" &&
                          transition.Reason == SeatVocabulary.ActivityReasonQuietTimeout);
        Assert.Contains(timedOut.Findings,
            finding => finding.Kind == SeatVocabulary.FindingActivityStale && finding.Open);
        Assert.Empty(timedOut.Effects);
        SeatAssert.Invariants(timedOut);

        AssertUnchanged(State(SessionValue.Present, ActivityValue.Working) with
        {
            LastEventAt = Now.AddMinutes(-9).AddSeconds(-59)
        }, new QuietTimeoutFired());
        AssertUnchanged(State(SessionValue.Present, ActivityValue.Working), new QuietTimeoutFired());
        AssertUnchanged(State(SessionValue.Present, ActivityValue.Idle) with
        {
            LastEventAt = Now.AddHours(-1)
        }, new QuietTimeoutFired());
        AssertUnchanged(State(SessionValue.Exited, ActivityValue.None) with
        {
            LastEventAt = Now.AddHours(-1)
        }, new QuietTimeoutFired());
    }

    [Fact]
    public void T9UnknownProlongedOpensAtFiveMinutesAndResolvesWhenReportedSessionRecovers()
    {
        var unknown = State(SessionValue.Unknown, ActivityValue.Unknown) with
        {
            SessionSince = Now.AddMinutes(-5)
        };
        var prolonged = Apply(unknown, new UnknownProlongedFired());

        Assert.Equal(unknown, prolonged.State);
        Assert.Contains(prolonged.Findings,
            finding => finding.Kind == SeatVocabulary.FindingStateUnknownProlonged && finding.Open);
        Assert.Empty(prolonged.Transitions);
        SeatAssert.Invariants(prolonged);

        AssertUnchanged(unknown with { SessionSince = Now.AddMinutes(-4).AddSeconds(-59) },
            new UnknownProlongedFired());
        AssertUnchanged(State(SessionValue.Present, ActivityValue.Idle) with
        {
            SessionSince = Now.AddHours(-1)
        }, new UnknownProlongedFired());

        var recovered = SeatStateMachine.Apply(unknown,
            new EventReceived(NodeId, unknown.NextSeq, 0, LaunchId,
                new HarnessBody(HarnessEventKind.SessionStarted, "native-1",
                    new Dictionary<string, string> { ["source"] = "resume" })),
            new ClaudeLike(), Now);
        Assert.Equal(SessionValue.Present, recovered.State.Session);
        Assert.Contains(recovered.Findings,
            finding => finding.Kind == SeatVocabulary.FindingStateUnknownProlonged && !finding.Open);
        SeatAssert.Invariants(recovered);

        var overlaid = unknown with
        {
            Overlay = SeatOverlay.NodeLinkLost,
            SessionReason = SeatVocabulary.SessionReasonNodeLinkLost,
            KnownSession = SessionValue.Present,
            KnownSessionReason = null,
            Activity = ActivityValue.Unknown,
            ActivityReason = SeatVocabulary.SessionReasonNodeLinkLost,
            KnownActivity = ActivityValue.Idle,
            KnownActivityReason = null
        };
        var overlayProlonged = Apply(overlaid, new UnknownProlongedFired());
        Assert.Contains(overlayProlonged.Findings,
            finding => finding.Kind == SeatVocabulary.FindingStateUnknownProlonged && finding.Open);
        SeatAssert.Invariants(overlayProlonged);
        var overlayRecovered = SeatStateMachine.Apply(overlaid,
            new NodeAttached(NodeId, null), new ClaudeLike(), Now);
        Assert.Equal(SessionValue.Present, overlayRecovered.State.Session);
        Assert.Contains(overlayRecovered.Findings,
            finding => finding.Kind == SeatVocabulary.FindingStateUnknownProlonged && !finding.Open);
        SeatAssert.Invariants(overlayRecovered);
    }

    private static SeatStep Apply(SeatState state, SeatInput input) =>
        SeatStateMachine.Apply(state, input, new ClaudeLike(), Now);

    private static void AssertUnchanged(SeatState state, SeatInput input)
    {
        var step = Apply(state, input);
        Assert.Equal(state, step.State);
        Assert.Empty(step.Transitions);
        Assert.Empty(step.Effects);
        Assert.DoesNotContain(step.Findings,
            finding => finding.Kind is SeatVocabulary.FindingActivityStale or
                SeatVocabulary.FindingStateUnknownProlonged && finding.Open);
        SeatAssert.Invariants(step);
    }

    private static SeatState State(SessionValue session, ActivityValue activity)
    {
        var sessionReason = session == SessionValue.Unknown ? SeatVocabulary.SessionReasonSourcesDisagree : null;
        var activityReason = activity == ActivityValue.Unknown
            ? session == SessionValue.Unknown
                ? SeatVocabulary.ActivityReasonSessionUnknown
                : SeatVocabulary.ActivityReasonQuietTimeout
            : null;
        return SeatState.Initial(Now) with
        {
            Session = session,
            KnownSession = session,
            SessionReason = sessionReason,
            KnownSessionReason = sessionReason,
            Activity = activity,
            KnownActivity = activity,
            ActivityReason = activityReason,
            KnownActivityReason = activityReason,
            Resumability = ResumabilityValue.Resumable,
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
            NativeSessionId = "native-1",
            NodeInstanceId = NodeId
        };
    }
}
