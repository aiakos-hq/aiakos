using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SequenceAttachmentOverlayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherNodeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherLaunchId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly IHarnessStateProfile Profile = new ClaudeLike();

    [Fact]
    public void FirstEventAdoptsNodeAndAdvancesSequenceWithoutGap()
    {
        var state = State() with { NodeInstanceId = null };
        var step = Apply(state, Event(OtherNodeId, 1, new UnknownBody()));

        Assert.Equal(OtherNodeId, step.State.NodeInstanceId);
        Assert.Equal(2, step.State.NextSeq);
        Assert.Empty(step.Findings);
        Assert.Empty(step.Effects);
        Assert.Equal(EventDisposition.Evidence, step.Disposition);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void SequenceGapMarksObservationLostAndRequestsCaptureOnce()
    {
        var state = State() with { KnownActivity = ActivityValue.Working, Activity = ActivityValue.Working,
            KnownActivityDetail = "tool:Bash", ActivityDetail = "tool:Bash", Resumability = ResumabilityValue.FreshOnly };
        var step = Apply(state, Event(NodeId, 2, new UnknownBody()));

        Assert.Equal(3, step.State.NextSeq);
        Assert.Equal(SessionValue.Present, step.State.KnownSession);
        Assert.Equal(ActivityValue.Unknown, step.State.KnownActivity);
        Assert.Equal(SeatVocabulary.ActivityReasonObservationGap, step.State.KnownActivityReason);
        Assert.Equal(ResumabilityValue.Unknown, step.State.Resumability);
        Assert.Equal(SeatVocabulary.ResumabilityReasonObservationGap, step.State.ResumabilityReason);
        Assert.Single(step.Findings, finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
        Assert.Single(step.Effects, effect => effect == new RequestCapture(LaunchId));
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(ActivityValue.Idle)]
    [InlineData(ActivityValue.Working)]
    [InlineData(ActivityValue.NeedsInput)]
    [InlineData(ActivityValue.Unknown)]
    public void A16GapMakesEveryPresentActivityUnknown(ActivityValue activity)
    {
        var state = State() with
        {
            KnownActivity = activity,
            Activity = activity,
            KnownActivityReason = activity == ActivityValue.Unknown ? "quiet-timeout" : null,
            ActivityReason = activity == ActivityValue.Unknown ? "quiet-timeout" : null,
            KnownActivityDetail = activity == ActivityValue.Working ? "tool:Bash" : null,
            ActivityDetail = activity == ActivityValue.Working ? "tool:Bash" : null,
        };
        var step = Apply(state, Event(NodeId, 2, new UnknownBody()));

        Assert.Equal(ActivityValue.Unknown, step.State.KnownActivity);
        Assert.Equal(SeatVocabulary.ActivityReasonObservationGap, step.State.KnownActivityReason);
        Assert.Contains(step.Transitions, transition => transition.Axis == "activity" && transition.Rule == "A16");
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(ResumabilityValue.None, ResumabilityValue.None, null)]
    [InlineData(ResumabilityValue.FreshOnly, ResumabilityValue.Unknown, SeatVocabulary.ResumabilityReasonObservationGap)]
    [InlineData(ResumabilityValue.Resumable, ResumabilityValue.Resumable, null)]
    [InlineData(ResumabilityValue.Lost, ResumabilityValue.Lost, null)]
    [InlineData(ResumabilityValue.Unknown, ResumabilityValue.Unknown, "existing-unknown")]
    public void U6GapChangesOnlyFreshOnlyResumability(ResumabilityValue initial,
        ResumabilityValue expected, string? expectedReason)
    {
        var state = State() with
        {
            Resumability = initial,
            ResumabilityReason = initial == ResumabilityValue.Unknown ? expectedReason : null,
        };
        var step = Apply(state, Event(NodeId, 2, new UnknownBody()));

        Assert.Equal(expected, step.State.Resumability);
        Assert.Equal(expectedReason, step.State.ResumabilityReason);
        Assert.Equal(initial == ResumabilityValue.FreshOnly,
            step.Transitions.Any(transition => transition.Axis == "resumability" && transition.Rule == "U6"));
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void DuplicateSequenceSkipsBodyAndLeavesEventTimeUnchanged()
    {
        var state = State() with { LastEventAt = Now.AddMinutes(-1) };
        var step = Apply(state, Event(NodeId, 0,
            new LaunchResultBody(LaunchOutcome.Failed, "failure", null)));

        Assert.Equal(EventDisposition.Duplicate, step.Disposition);
        Assert.Equal(state.KnownSession, step.State.KnownSession);
        Assert.Equal(state.Resumability, step.State.Resumability);
        Assert.Equal(state.LastEventAt, step.State.LastEventAt);
        Assert.Empty(step.Transitions);
        Assert.Empty(step.Findings);
        Assert.Empty(step.Effects);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void NewNodeInstanceAppliesOneGapThenResetsSequence()
    {
        var state = State() with
        {
            KnownActivity = ActivityValue.Working,
            Activity = ActivityValue.Working,
            Resumability = ResumabilityValue.FreshOnly,
        };
        var step = Apply(state, Event(OtherNodeId, 1, new UnknownBody()));

        Assert.Equal(OtherNodeId, step.State.NodeInstanceId);
        Assert.Equal(2, step.State.NextSeq);
        Assert.Equal(ActivityValue.Unknown, step.State.KnownActivity);
        Assert.Equal(ResumabilityValue.Unknown, step.State.Resumability);
        Assert.Single(step.Findings, finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
        Assert.Single(step.Effects, effect => effect == new RequestCapture(LaunchId));
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void EpochSequenceAndExplicitGapCoalesceToOneObservationGap()
    {
        var state = State() with
        {
            KnownActivity = ActivityValue.Idle,
            Activity = ActivityValue.Idle,
            Resumability = ResumabilityValue.FreshOnly,
        };
        var step = Apply(state, Event(OtherNodeId, 2,
            new ObservationGapBody(GapReason.BufferOverflow), Guid.NewGuid()));

        Assert.Equal(3, step.State.NextSeq);
        Assert.Equal(OtherNodeId, step.State.NodeInstanceId);
        Assert.Equal(1, step.Findings.Count(finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open));
        Assert.Equal(1, step.Effects.Count(effect => effect == new RequestCapture(LaunchId)));
        Assert.Equal(1, step.Transitions.Count(transition => transition.Axis == "activity" && transition.Rule == "A16"));
        Assert.Equal(1, step.Transitions.Count(transition => transition.Axis == "resumability" && transition.Rule == "U6"));
        Assert.Equal(EventDisposition.StaleLaunch, step.Disposition);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SeatOverlay.NodeLinkLost, SeatVocabulary.SessionReasonNodeLinkLost)]
    [InlineData(SeatOverlay.OrchestratorRestarted, SeatVocabulary.SessionReasonOrchestratorRestarted)]
    public void OverlayHidesKnownSessionAndActivity(SeatOverlay overlay, string reason)
    {
        var step = Apply(State() with { KnownActivity = ActivityValue.Working,
            KnownActivityDetail = "tool:Bash", Activity = ActivityValue.Working,
            ActivityDetail = "tool:Bash" }, OverlayInput(overlay));

        Assert.Equal(overlay, step.State.Overlay);
        Assert.Equal(SessionValue.Present, step.State.KnownSession);
        Assert.Equal(ActivityValue.Working, step.State.KnownActivity);
        Assert.Equal("tool:Bash", step.State.KnownActivityDetail);
        Assert.Equal(SessionValue.Unknown, step.State.Session);
        Assert.Equal(ActivityValue.Unknown, step.State.Activity);
        Assert.Null(step.State.ActivityDetail);
        Assert.Equal(reason, step.State.SessionReason);
        Assert.Equal(reason, step.State.ActivityReason);
        Assert.All(step.Transitions, transition => Assert.Equal("R16", transition.Rule));
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void SecondOverlayChangesOnlyOverlayAndReportedReasons()
    {
        var first = Apply(State(), new NodeLinkLost());
        var step = SeatStateMachine.Apply(first.State, new OrchestratorRestarted(), Profile, Now.AddMinutes(1));

        Assert.Equal(SeatOverlay.OrchestratorRestarted, step.State.Overlay);
        Assert.Equal(first.State.KnownSession, step.State.KnownSession);
        Assert.Equal(first.State.KnownActivity, step.State.KnownActivity);
        Assert.Equal(SeatVocabulary.SessionReasonOrchestratorRestarted, step.State.SessionReason);
        Assert.Equal(SeatVocabulary.SessionReasonOrchestratorRestarted, step.State.ActivityReason);
        Assert.Equal(first.State.SessionSince, step.State.SessionSince);
        Assert.Equal(first.State.ActivitySince, step.State.ActivitySince);
        Assert.All(step.Transitions, transition => Assert.Equal("R16", transition.Rule));
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Absent)]
    [InlineData(SessionValue.Exited)]
    public void OverlayDoesNotApplyToAbsentOrExitedSessions(SessionValue session)
    {
        var state = State(session);
        var step = Apply(state, new NodeLinkLost());

        Assert.Equal(state, step.State);
        Assert.Empty(step.Transitions);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionLifecycle.Launching, SessionValue.Starting, ActivityValue.Unknown, SeatVocabulary.ActivityReasonNotReady)]
    [InlineData(SessionLifecycle.Running, SessionValue.Present, ActivityValue.Unknown, SeatVocabulary.ActivityReasonObservationGap)]
    [InlineData(SessionLifecycle.Exited, SessionValue.Exited, ActivityValue.None, null)]
    [InlineData(SessionLifecycle.Unknown, SessionValue.Unknown, ActivityValue.Unknown, SeatVocabulary.ActivityReasonSessionUnknown)]
    [InlineData(SessionLifecycle.Unspecified, SessionValue.Unknown, ActivityValue.Unknown, SeatVocabulary.ActivityReasonSessionUnknown)]
    [InlineData((SessionLifecycle)99, SessionValue.Unknown, ActivityValue.Unknown, SeatVocabulary.ActivityReasonSessionUnknown)]
    public void S15ReconcilesMatchingInventory(SessionLifecycle lifecycle, SessionValue expectedSession,
        ActivityValue expectedActivity, string? expectedActivityReason)
    {
        var state = State() with { ReadinessSeen = true };
        var step = Apply(state, new NodeAttached(OtherNodeId,
            new SeatInventoryEntry(LaunchId, lifecycle, 8)));
        Assert.Null(step.Disposition);

        Assert.Equal(expectedSession, step.State.KnownSession);
        Assert.Equal(expectedActivity, step.State.KnownActivity);
        Assert.Equal(expectedActivityReason, step.State.KnownActivityReason);
        Assert.Equal(lifecycle != SessionLifecycle.Launching, step.State.ReadinessSeen);
        Assert.Equal(1, step.State.NextSeq);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingInventoryMismatch && !finding.Open);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
        if (lifecycle == SessionLifecycle.Exited)
            Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingUnexpectedExit && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void S15SameInstanceDoesNotReconcileItsInventory()
    {
        var state = State(SessionValue.Present);
        var step = Apply(state, new NodeAttached(NodeId,
            new SeatInventoryEntry(OtherLaunchId, SessionLifecycle.Exited, 4)));
        Assert.Null(step.Disposition);

        Assert.Equal(state.KnownSession, step.State.KnownSession);
        Assert.Equal(state.KnownActivity, step.State.KnownActivity);
        Assert.Equal(state.NextSeq, step.State.NextSeq);
        Assert.DoesNotContain(step.Transitions, transition => transition.Rule == "S15" || transition.Rule == "S16");
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void S15PreservesAbsentSessionDespiteMatchingInventory()
    {
        var state = State(SessionValue.Absent);
        var step = Apply(state, new NodeAttached(OtherNodeId,
            new SeatInventoryEntry(LaunchId, SessionLifecycle.Running, 8)));
        Assert.Null(step.Disposition);

        Assert.Equal(SessionValue.Absent, step.State.KnownSession);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingInventoryMismatch && !finding.Open);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void NodeAttachWithNoPreviousInstanceAdoptsIdAndIgnoresInventory()
    {
        var state = State() with { NodeInstanceId = null };
        var step = Apply(state, new NodeAttached(OtherNodeId,
            new SeatInventoryEntry(LaunchId, SessionLifecycle.Exited, 8)));
        Assert.Null(step.Disposition);

        Assert.Equal(OtherNodeId, step.State.NodeInstanceId);
        Assert.Equal(state.KnownSession, step.State.KnownSession);
        Assert.Equal(state.NextSeq, step.State.NextSeq);
        Assert.Empty(step.Effects);
        Assert.DoesNotContain(step.Findings, finding => finding.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Absent, SessionValue.Absent, null, false)]
    [InlineData(SessionValue.Starting, SessionValue.Unknown, SeatVocabulary.SessionReasonInventoryMissing, true)]
    [InlineData(SessionValue.Present, SessionValue.Unknown, SeatVocabulary.SessionReasonInventoryMissing, true)]
    [InlineData(SessionValue.Exited, SessionValue.Exited, null, false)]
    [InlineData(SessionValue.Unknown, SessionValue.Unknown, SeatVocabulary.SessionReasonInventoryMissing, true)]
    public void S16MissingOrDifferentInventoryReconcilesKnownSession(SessionValue initial,
        SessionValue expected, string? expectedReason, bool inventoryMismatch)
    {
        var step = Apply(State(initial), new NodeAttached(OtherNodeId,
            new SeatInventoryEntry(OtherLaunchId, SessionLifecycle.Running, 8)));
        Assert.Null(step.Disposition);

        Assert.Equal(expected, step.State.KnownSession);
        Assert.Equal(expectedReason, step.State.KnownSessionReason);
        Assert.Equal(inventoryMismatch, step.Findings.Any(finding =>
            finding.Kind == SeatVocabulary.FindingInventoryMismatch && finding.Open));
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void S16MissingInventoryUsesTheSameReconciliation()
    {
        var step = Apply(State(SessionValue.Present), new NodeAttached(OtherNodeId, null));
        Assert.Null(step.Disposition);

        Assert.Equal(SessionValue.Unknown, step.State.KnownSession);
        Assert.Equal(SeatVocabulary.SessionReasonInventoryMissing, step.State.KnownSessionReason);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingInventoryMismatch && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void SameInstanceAttachmentWaitsForCatchupThenRestoresKnownState()
    {
        var state = State() with
        {
            NextSeq = 2,
            Overlay = SeatOverlay.NodeLinkLost,
            Session = SessionValue.Unknown,
            SessionReason = SeatVocabulary.SessionReasonNodeLinkLost,
            Activity = ActivityValue.Unknown,
            ActivityReason = SeatVocabulary.SessionReasonNodeLinkLost,
            KnownActivity = ActivityValue.Working,
            ActivityDetail = null,
        };
        var attached = Apply(state, new NodeAttached(NodeId,
            new SeatInventoryEntry(LaunchId, SessionLifecycle.Exited, 2)));
        Assert.Null(attached.Disposition);

        Assert.Equal(SeatOverlay.NodeLinkLost, attached.State.Overlay);
        Assert.Equal(2, attached.State.CatchUpSeq);
        Assert.Equal(SessionValue.Present, attached.State.KnownSession);
        Assert.Contains(attached.Findings, finding => finding.Kind == SeatVocabulary.FindingNodeNotConnected && !finding.Open);
        var caughtUp = Apply(attached.State, Event(NodeId, 2, new UnknownBody()));

        Assert.Equal(EventDisposition.Evidence, caughtUp.Disposition);
        Assert.Null(caughtUp.State.Overlay);
        Assert.Null(caughtUp.State.CatchUpSeq);
        Assert.Equal(caughtUp.State.KnownSession, caughtUp.State.Session);
        Assert.Equal(caughtUp.State.KnownActivity, caughtUp.State.Activity);
        Assert.Contains(caughtUp.Transitions, transition => transition.Rule == "R17");
        SeatAssert.Invariants(caughtUp);
    }

    [Fact]
    public void SameInstanceAttachmentClearsOverlayImmediatelyWhenAlreadyCaughtUp()
    {
        var state = State() with
        {
            NextSeq = 4,
            Overlay = SeatOverlay.NodeLinkLost,
            Session = SessionValue.Unknown,
            SessionReason = SeatVocabulary.SessionReasonNodeLinkLost,
            Activity = ActivityValue.Unknown,
            ActivityReason = SeatVocabulary.SessionReasonNodeLinkLost,
        };
        var step = Apply(state, new NodeAttached(NodeId,
            new SeatInventoryEntry(OtherLaunchId, SessionLifecycle.Exited, 3)));
        Assert.Null(step.Disposition);

        Assert.Null(step.State.Overlay);
        Assert.Null(step.State.CatchUpSeq);
        Assert.Equal(step.State.KnownSession, step.State.Session);
        Assert.Contains(step.Transitions, transition => transition.Rule == "R17");
        SeatAssert.Invariants(step);
    }

    private static SeatStep Apply(SeatState state, SeatInput input) =>
        SeatStateMachine.Apply(state, input, Profile, Now);

    private static SeatInput OverlayInput(SeatOverlay overlay) => overlay switch
    {
        SeatOverlay.NodeLinkLost => new NodeLinkLost(),
        SeatOverlay.OrchestratorRestarted => new OrchestratorRestarted(),
        _ => throw new ArgumentOutOfRangeException(nameof(overlay))
    };

    private static EventReceived Event(Guid nodeId, long seq, SeatEventBody body, Guid? launchId = null) =>
        new(nodeId, seq, 0, launchId ?? LaunchId, body);

    private static SeatState State(SessionValue session = SessionValue.Present) =>
        HarnessReadinessTests.State(session) with
        {
            KnownActivity = session is SessionValue.Absent or SessionValue.Exited ? ActivityValue.None : ActivityValue.Idle,
            Activity = session is SessionValue.Absent or SessionValue.Exited ? ActivityValue.None : ActivityValue.Idle,
            KnownActivityReason = session == SessionValue.Starting ? SeatVocabulary.ActivityReasonNotReady :
                session == SessionValue.Unknown ? SeatVocabulary.ActivityReasonSessionUnknown : null,
            ActivityReason = session == SessionValue.Starting ? SeatVocabulary.ActivityReasonNotReady :
                session == SessionValue.Unknown ? SeatVocabulary.ActivityReasonSessionUnknown : null,
            Resumability = ResumabilityValue.Resumable,
            ResumabilityReason = null,
            Desired = SeatDesired.Up,
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
            NodeInstanceId = NodeId,
            NextSeq = 1,
        };
}
