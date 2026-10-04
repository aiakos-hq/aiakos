using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SessionTableTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IHarnessStateProfile Profile = new TestProfile();

    [Theory]
    [InlineData(SessionValue.Starting, StopOutcome.Stopped, SessionValue.Absent)]
    [InlineData(SessionValue.Present, StopOutcome.Killed, SessionValue.Absent)]
    [InlineData(SessionValue.Exited, StopOutcome.NotRunning, SessionValue.Absent)]
    [InlineData(SessionValue.Unknown, StopOutcome.NotRunning, SessionValue.Absent)]
    public void S3(SessionValue initial, StopOutcome outcome, SessionValue expected)
    {
        var step = Apply(State(initial), new EventReceived(NodeId, 1, 0, LaunchId, new StopResultBody(outcome)));

        Assert.Equal(expected, step.State.KnownSession);
        Assert.Equal(ActivityValue.None, step.State.KnownActivity);
        Assert.Contains(step.Transitions, t => t.Axis == "session" && t.Rule == "S3");
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Starting)]
    [InlineData(SessionValue.Present)]
    public void S4(SessionValue initial)
    {
        var step = Apply(State(initial), new EventReceived(NodeId, 1, 0, LaunchId,
            new StopNotCompletedBody(CommandStatus.TimedOut)));

        Assert.Equal(SessionValue.Unknown, step.State.KnownSession);
        Assert.Equal(SeatVocabulary.SessionReasonStopFailed, step.State.KnownSessionReason);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Starting, false, SessionValue.Present, ActivityValue.Unknown)]
    [InlineData(SessionValue.Starting, true, SessionValue.Present, ActivityValue.Unknown)]
    [InlineData(SessionValue.Unknown, false, SessionValue.Present, ActivityValue.Unknown)]
    [InlineData(SessionValue.Unknown, true, SessionValue.Present, ActivityValue.Unknown)]
    [InlineData(SessionValue.Exited, false, SessionValue.Unknown, ActivityValue.Unknown)]
    public void S5(SessionValue initial, bool readinessSeen,
        SessionValue expected, ActivityValue activity)
    {
        var state = State(initial) with { ReadinessSeen = readinessSeen };
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Ready, "", null)));

        Assert.Equal(expected, step.State.KnownSession);
        Assert.Equal(activity, step.State.KnownActivity);
        if (initial == SessionValue.Exited ||
            (initial is SessionValue.Starting or SessionValue.Unknown) && !readinessSeen)
            Assert.Contains(step.Findings, f => f.Kind == SeatVocabulary.FindingSourcesDisagree && f.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Starting, SessionValue.Exited)]
    [InlineData(SessionValue.Present, SessionValue.Unknown)]
    [InlineData(SessionValue.Unknown, SessionValue.Exited)]
    public void S6(SessionValue initial, SessionValue expected)
    {
        var step = Apply(State(initial), new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Failed, "failed", null)));

        Assert.Equal(expected, step.State.KnownSession);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(LaunchOutcome.Unknown, SessionValue.Starting, SessionValue.Unknown, SeatVocabulary.SessionReasonLaunchUnconfirmed)]
    [InlineData(LaunchOutcome.Unspecified, SessionValue.Starting, SessionValue.Unknown, SeatVocabulary.SessionReasonLaunchUnconfirmed)]
    [InlineData((LaunchOutcome)99, SessionValue.Starting, SessionValue.Unknown, SeatVocabulary.SessionReasonLaunchUnconfirmed)]
    [InlineData(LaunchOutcome.Unknown, SessionValue.Present, SessionValue.Unknown, SeatVocabulary.SessionReasonSourcesDisagree)]
    public void S7(LaunchOutcome outcome, SessionValue initial, SessionValue expected, string reason)
    {
        var step = Apply(State(initial), new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(outcome, "", null)));

        Assert.Equal(expected, step.State.KnownSession);
        Assert.Equal(reason, step.State.KnownSessionReason);
        var finding = initial == SessionValue.Present
            ? SeatVocabulary.FindingSourcesDisagree
            : SeatVocabulary.FindingLaunchUnconfirmed;
        Assert.Contains(step.Findings, f => f.Kind == finding && f.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SeatCommandKind.Start, SessionValue.Starting, SessionValue.Absent)]
    [InlineData(SeatCommandKind.Stop, SessionValue.Present, SessionValue.Unknown)]
    public void DispatchFailureAppliesStartOrStopResult(SeatCommandKind kind, SessionValue initial,
        SessionValue expected)
    {
        var step = SeatStateMachine.Apply(State(initial), new CommandDispatchFailed(kind), Profile, Now);
        Assert.Equal(expected, step.State.KnownSession);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void WatchdogOnlyChangesStartingLaunch()
    {
        var starting = SeatStateMachine.Apply(State(SessionValue.Starting), new LaunchWatchdogFired(), Profile, Now);
        var present = SeatStateMachine.Apply(State(SessionValue.Present), new LaunchWatchdogFired(), Profile, Now);
        Assert.Equal(SessionValue.Unknown, starting.State.KnownSession);
        Assert.Equal(SeatVocabulary.SessionReasonLaunchResultMissing, starting.State.KnownSessionReason);
        Assert.Contains(starting.Findings, f => f.Kind == SeatVocabulary.FindingLaunchUnconfirmed && f.Open);
        Assert.Equal(SessionValue.Present, present.State.KnownSession);
        SeatAssert.Invariants(starting);
        SeatAssert.Invariants(present);
    }

    [Theory]
    [InlineData(SessionValue.Starting)]
    [InlineData(SessionValue.Present)]
    [InlineData(SessionValue.Unknown)]
    public void S14(SessionValue initial)
    {
        var step = Apply(State(initial), new EventReceived(NodeId, 1, 0, LaunchId,
            new ProcessExitedBody(3, null)));
        Assert.Equal(SessionValue.Exited, step.State.KnownSession);
        Assert.Equal(ActivityValue.None, step.State.KnownActivity);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void OldLaunchEventIsStaleAndDoesNotChangeAxes()
    {
        var state = State(SessionValue.Present);
        var step = Apply(state, new EventReceived(NodeId, 1, 0, Guid.NewGuid(),
            new LaunchResultBody(LaunchOutcome.Failed, "", null)));
        Assert.Equal(EventDisposition.StaleLaunch, step.Disposition);
        Assert.Equal(state.KnownSession, step.State.KnownSession);
        SeatAssert.Invariants(step);
    }

    private static SeatStep Apply(SeatState state, SeatInput input) =>
        SeatStateMachine.Apply(state, input, Profile, Now);

    private static SeatState State(SessionValue session) => SeatState.Initial(Now) with
    {
        Session = session,
        KnownSession = session,
        SessionReason = session == SessionValue.Unknown ? "test" : null,
        KnownSessionReason = session == SessionValue.Unknown ? "test" : null,
        Activity = session is SessionValue.Exited or SessionValue.Absent ? ActivityValue.None : ActivityValue.Idle,
        KnownActivity = session is SessionValue.Exited or SessionValue.Absent ? ActivityValue.None : ActivityValue.Idle,
        ActivityReason = null,
        KnownActivityReason = null,
        Desired = SeatDesired.Up,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NodeInstanceId = NodeId
    };

    private sealed class TestProfile : IHarnessStateProfile
    {
        public string Harness => "test";
        public string NewNativeSessionId() => "new";
        public bool IsValidNativeSessionId(string id) => true;
        public bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => false;
        public bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => false;
        public bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => false;
        public bool FreshRelaunchReusesSessionId => false;
        public bool EmitsInputResolved => false;
        public TimeSpan ReadyTimeout => TimeSpan.FromMinutes(1);
        public TimeSpan ConfirmTimeout => TimeSpan.FromMinutes(1);
        public TimeSpan QuietTimeout => TimeSpan.FromMinutes(1);
    }
}
