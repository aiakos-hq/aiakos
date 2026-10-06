using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class LaunchDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NewLaunchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Theory]
    [InlineData(ResumabilityValue.None, false, LaunchMode.Fresh, SeatVocabulary.DecisionNewSession, false, true)]
    [InlineData(ResumabilityValue.None, true, LaunchMode.Fresh, SeatVocabulary.DecisionNewSession, false, true)]
    [InlineData(ResumabilityValue.FreshOnly, false, LaunchMode.Fresh, SeatVocabulary.DecisionNoConversationYet, true, false)]
    [InlineData(ResumabilityValue.FreshOnly, true, LaunchMode.Fresh, SeatVocabulary.DecisionFreshExplicit, false, true)]
    [InlineData(ResumabilityValue.Resumable, false, LaunchMode.Resume, SeatVocabulary.DecisionResume, false, false)]
    [InlineData(ResumabilityValue.Resumable, true, LaunchMode.Fresh, SeatVocabulary.DecisionFreshExplicit, false, true)]
    [InlineData(ResumabilityValue.Lost, true, LaunchMode.Fresh, SeatVocabulary.DecisionFreshExplicit, false, true)]
    [InlineData(ResumabilityValue.Unknown, false, LaunchMode.Resume, SeatVocabulary.DecisionResumeUnverified, false, false)]
    [InlineData(ResumabilityValue.Unknown, true, LaunchMode.Fresh, SeatVocabulary.DecisionFreshExplicit, false, true)]
    public void LD1ToLD5ChooseLaunchModeAndSession(ResumabilityValue resumability, bool fresh,
        LaunchMode mode, string decision, bool reusedClaudeId, bool newSession)
    {
        foreach (var profile in Profiles())
        {
            var existingId = resumability == ResumabilityValue.None ? null : "native-1";
            var state = State(SessionValue.Absent, resumability) with
            {
                Session = SessionValue.Absent,
                NativeSessionId = existingId
            };
            var step = Apply(state, new UpRequested(fresh, true, NewLaunchId, "native-new"), profile);

            Assert.IsType<Accepted>(step.Reply);
            var start = Assert.IsType<StartLaunch>(Assert.Single(step.Effects));
            Assert.Equal(mode, start.Mode);
            Assert.Equal(decision, start.Decision);
            Assert.Equal(NewLaunchId, start.LaunchId);
            var expectedNewSession = resumability == ResumabilityValue.FreshOnly && !fresh
                ? !profile.FreshRelaunchReusesSessionId
                : newSession;
            Assert.Equal(expectedNewSession, start.NewSession);
            var expectedNativeId = mode == LaunchMode.Resume ||
                reusedClaudeId && profile.FreshRelaunchReusesSessionId ? "native-1" : "native-new";
            Assert.Equal(expectedNativeId, start.NativeSessionId);
            Assert.Equal(existingId is not null && expectedNativeId != existingId, start.AbandonPreviousSession);
            Assert.Equal(start.NativeSessionId, step.State.NativeSessionId);
            Assert.Equal(new CurrentLaunch(NewLaunchId, mode,
                reusedClaudeId && profile.FreshRelaunchReusesSessionId, false), step.State.Launch);
            Assert.Equal(SeatDesired.Up, step.State.Desired);
            Assert.Equal(SessionValue.Starting, step.State.KnownSession);
            Assert.Equal(newSession ? ResumabilityValue.FreshOnly : resumability, step.State.Resumability);
            Assert.False(step.State.ReadinessSeen);
            Assert.Null(step.State.PendingInputRequest);
            Assert.Null(step.State.PreCompactionActivity);
            SeatAssert.Invariants(step);
        }
    }

    [Fact]
    public void NormalUpFromLostIsRejectedWithoutChangingState()
    {
        var state = State(SessionValue.Absent, ResumabilityValue.Lost) with
        {
            Session = SessionValue.Absent,
            NativeSessionId = "native-1"
        };
        var step = Apply(state, new UpRequested(false, true, NewLaunchId, "native-new"), new ClaudeLike());
        Assert.Equal(new Rejected(SeatVocabulary.RejectionResumeLost), step.Reply);
        Assert.Equal(state, step.State);
        Assert.Empty(step.Effects);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void ReplacingExistingNativeSessionAbandonsThePreviousSession()
    {
        var state = State(SessionValue.Absent, ResumabilityValue.FreshOnly) with
        {
            Session = SessionValue.Absent,
            NativeSessionId = "native-old"
        };

        var step = Apply(state, new UpRequested(false, true, NewLaunchId, "native-new"), new OpenCodeLike());

        var launch = Assert.IsType<StartLaunch>(Assert.Single(step.Effects));
        Assert.Equal("native-new", launch.NativeSessionId);
        Assert.True(launch.AbandonPreviousSession);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void ConnectivityThenReportedSessionControlUpRejectionsAndAlreadyUp()
    {
        var absent = State(SessionValue.Absent, ResumabilityValue.None);
        var disconnected = Apply(absent, new UpRequested(false, false, NewLaunchId, "native-new"), new ClaudeLike());
        Assert.Equal(new Rejected("NODE_NOT_CONNECTED"), disconnected.Reply);
        Assert.Equal(absent, disconnected.State);
        Assert.Contains(disconnected.Findings, finding => finding.Kind == SeatVocabulary.FindingNodeNotConnected && finding.Open);

        var unknown = State(SessionValue.Unknown, ResumabilityValue.None);
        var rejectedUnknown = Apply(unknown, new UpRequested(false, true, NewLaunchId, "native-new"), new ClaudeLike());
        Assert.Equal(new Rejected(SeatVocabulary.RejectionSeatStateUnknown), rejectedUnknown.Reply);
        Assert.Equal(unknown, rejectedUnknown.State);

        foreach (var session in new[] { SessionValue.Starting, SessionValue.Present })
        {
            var state = State(session, ResumabilityValue.None) with { Desired = SeatDesired.Down };
            var step = Apply(state, new UpRequested(false, true, NewLaunchId, "native-new"), new ClaudeLike());
            Assert.IsType<AlreadyUp>(step.Reply);
            Assert.Equal(SeatDesired.Up, step.State.Desired);
            Assert.Equal(state.Launch, step.State.Launch);
            Assert.Empty(step.Effects);
            SeatAssert.Invariants(step);
        }
        SeatAssert.Invariants(disconnected);
        SeatAssert.Invariants(rejectedUnknown);
    }

    [Fact]
    public void DownDispatchesStopForKnownNonAbsentLaunchAndResolvesFindings()
    {
        var state = State(SessionValue.Present, ResumabilityValue.Resumable) with
        {
            PendingInputRequest = "pending",
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Resume, false, false)
        };
        var step = Apply(state, new DownRequested(), new ClaudeLike());
        Assert.IsType<Accepted>(step.Reply);
        Assert.Equal(SeatDesired.Down, step.State.Desired);
        Assert.Equal(state.Launch with { StopRequested = true }, step.State.Launch);
        Assert.Equal(new SeatEffect[] { new DispatchStop(LaunchId) }, step.Effects);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingUnexpectedExit && !finding.Open);
        SeatAssert.Invariants(step);

        var absent = State(SessionValue.Absent, ResumabilityValue.None) with { Launch = null };
        var noStop = Apply(absent, new DownRequested(), new ClaudeLike());
        Assert.IsType<Accepted>(noStop.Reply);
        Assert.Equal(SeatDesired.Down, noStop.State.Desired);
        Assert.Empty(noStop.Effects);
        SeatAssert.Invariants(noStop);
    }

    [Fact]
    public void DownStopResultResolvesOrphanHarness()
    {
        var state = State(SessionValue.Present, ResumabilityValue.Resumable);
        var down = Apply(state, new DownRequested(), new ClaudeLike());
        var stopped = SeatStateMachine.Apply(down.State,
            new EventReceived(Guid.Empty, 1, 0, LaunchId, new StopResultBody(StopOutcome.Stopped)), new ClaudeLike(), Now);
        Assert.Equal(SessionValue.Absent, stopped.State.KnownSession);
        Assert.Contains(stopped.Findings, finding => finding.Kind == SeatVocabulary.FindingOrphanHarness && !finding.Open);
        SeatAssert.Invariants(stopped);
    }

    [Theory]
    [InlineData(ResumabilityValue.None)]
    [InlineData(ResumabilityValue.Lost)]
    [InlineData(ResumabilityValue.Unknown)]
    public void U1NewSessionAssignmentSetsFreshOnly(ResumabilityValue initial)
    {
        var state = State(SessionValue.Absent, initial);
        var step = Apply(state, new UpRequested(true, true, NewLaunchId, "native-new"), new OpenCodeLike());
        Assert.Equal(ResumabilityValue.FreshOnly, step.State.Resumability);
        Assert.Equal("U1", Assert.Single(step.Transitions, transition => transition.Axis == "resumability").Rule);
        SeatAssert.Invariants(step);
    }

    private static IEnumerable<IHarnessStateProfile> Profiles() => [new ClaudeLike(), new OpenCodeLike()];

    private static SeatStep Apply(SeatState state, SeatInput input, IHarnessStateProfile profile) =>
        SeatStateMachine.Apply(state, input, profile, Now);

    private static SeatState State(SessionValue session, ResumabilityValue resumability) => SeatState.Initial(Now) with
    {
        Session = session,
        KnownSession = session,
        Resumability = resumability,
        SessionReason = session == SessionValue.Unknown ? "test" : null,
        KnownSessionReason = session == SessionValue.Unknown ? "test" : null,
        Activity = session is SessionValue.Exited or SessionValue.Absent ? ActivityValue.None : ActivityValue.Idle,
        KnownActivity = session is SessionValue.Exited or SessionValue.Absent ? ActivityValue.None : ActivityValue.Idle,
        Desired = SeatDesired.Down,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NativeSessionId = "native-old",
        PendingInputRequest = "old-pending",
        PreCompactionActivity = ActivityValue.Working
    };
}
