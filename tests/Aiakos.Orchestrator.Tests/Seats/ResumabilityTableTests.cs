using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class ResumabilityTableTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IReadOnlyDictionary<string, string> RotationAttributes =
        HarnessReadinessTests.Attrs(("source", "clear"), ("previous_session_id", "native-1"));
    private static readonly IReadOnlyDictionary<string, string> MismatchedRotationAttributes =
        HarnessReadinessTests.Attrs(("source", "clear"), ("previous_session_id", "not-native-1"));
    private static readonly IHarnessStateProfile Profile = new ClaudeLike();

    [Theory]
    [InlineData(ResumabilityValue.None, ResumabilityValue.None, null, false)]
    [InlineData(ResumabilityValue.FreshOnly, ResumabilityValue.Resumable, null, false)]
    [InlineData(ResumabilityValue.Resumable, ResumabilityValue.Resumable, null, false)]
    [InlineData(ResumabilityValue.Lost, ResumabilityValue.Unknown, SeatVocabulary.ResumabilityReasonContradictingEvidence, true)]
    [InlineData(ResumabilityValue.Unknown, ResumabilityValue.Resumable, null, false)]
    public void U2MatchingConversationEvidenceUpdatesResumability(ResumabilityValue initial,
        ResumabilityValue expected, string? expectedReason, bool opensSourcesDisagree)
    {
        var state = State(SessionValue.Present, initial);
        var step = Apply(state, HarnessReadinessTests.Harness(HarnessEventKind.PromptSubmitted, "native-1",
            HarnessReadinessTests.Attrs()));

        Assert.Equal(expected, step.State.Resumability);
        Assert.Equal(expectedReason, step.State.ResumabilityReason);
        Assert.Equal(opensSourcesDisagree, step.Findings.Any(finding =>
            finding.Kind == SeatVocabulary.FindingSourcesDisagree && finding.Open));
        if (initial != expected)
            Assert.Equal("U2", Assert.Single(step.Transitions, transition => transition.Axis == "resumability").Rule);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void U2EvidenceForAnotherNativeSessionDoesNotChangeResumability()
    {
        var state = State(SessionValue.Present, ResumabilityValue.FreshOnly);
        var step = Apply(state, HarnessReadinessTests.Harness(HarnessEventKind.PromptSubmitted, "native-other",
            HarnessReadinessTests.Attrs()));

        Assert.Equal(ResumabilityValue.FreshOnly, step.State.Resumability);
        Assert.DoesNotContain(step.Transitions, transition => transition.Axis == "resumability");
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void U2UsesTheSuppliedHarnessProfileToRecognizeEvidence()
    {
        var state = State(SessionValue.Present, ResumabilityValue.FreshOnly);
        var input = HarnessReadinessTests.Harness(HarnessEventKind.SessionStarted, "native-1",
            HarnessReadinessTests.Attrs());
        var step = SeatStateMachine.Apply(state, input, new OpenCodeLike(), Now);

        Assert.Equal(ResumabilityValue.Resumable, step.State.Resumability);
        Assert.Contains(step.Transitions, transition => transition.Axis == "resumability" && transition.Rule == "U2");
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void U3ReadyResumeMakesUnknownResumabilityResumable()
    {
        var state = State(SessionValue.Starting, ResumabilityValue.Unknown) with
        {
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Resume, false, false),
        };
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Ready, "", null)));

        Assert.Equal(ResumabilityValue.Resumable, step.State.Resumability);
        Assert.Null(step.State.ResumabilityReason);
        Assert.Contains(step.Transitions, transition => transition.Axis == "resumability" && transition.Rule == "U3");
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void U3ReadyFreshLaunchDoesNotChangeUnknownResumability()
    {
        var state = State(SessionValue.Starting, ResumabilityValue.Unknown);
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Ready, "", null)));

        Assert.Equal(ResumabilityValue.Unknown, step.State.Resumability);
        Assert.Equal(state.ResumabilityReason, step.State.ResumabilityReason);
        Assert.DoesNotContain(step.Transitions, transition => transition.Axis == "resumability");
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(ResumabilityValue.Resumable, ResumabilityValue.Lost)]
    [InlineData(ResumabilityValue.Unknown, ResumabilityValue.Lost)]
    [InlineData(ResumabilityValue.Lost, ResumabilityValue.Lost)]
    public void U4MissingResumeSessionMarksResumabilityLost(ResumabilityValue initial, ResumabilityValue expected)
    {
        var state = State(SessionValue.Present, initial) with
        {
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Resume, false, false),
        };
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Failed, SeatVocabulary.LaunchReasonResumeSessionNotFound, null)));

        Assert.Equal(expected, step.State.Resumability);
        Assert.Null(step.State.ResumabilityReason);
        Assert.Equal(initial == ResumabilityValue.Resumable, step.Findings.Any(finding =>
            finding.Kind == SeatVocabulary.FindingResumeLost && finding.Open));
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void U5FailedFreshRelaunchThatReusedIdMakesFreshOnlyUnknown()
    {
        var state = State(SessionValue.Starting, ResumabilityValue.FreshOnly) with
        {
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, true, false),
        };
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Failed, "OTHER_FAILURE", null)));

        Assert.Equal(ResumabilityValue.Unknown, step.State.Resumability);
        Assert.Equal(SeatVocabulary.ResumabilityReasonFreshRelaunchFailed, step.State.ResumabilityReason);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingLaunchFailed && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void U17bOtherFailedResumeReasonLeavesResumabilityUnchangedAndOpensLaunchFailed()
    {
        var state = State(SessionValue.Starting, ResumabilityValue.FreshOnly) with
        {
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Resume, false, false),
        };
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new LaunchResultBody(LaunchOutcome.Failed, "OTHER_FAILURE", null)));

        Assert.Equal(ResumabilityValue.FreshOnly, step.State.Resumability);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingLaunchFailed && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(ResumabilityValue.None, ResumabilityValue.None, null, false)]
    [InlineData(ResumabilityValue.FreshOnly, ResumabilityValue.Unknown, SeatVocabulary.ResumabilityReasonSessionIdMismatch, true)]
    [InlineData(ResumabilityValue.Resumable, ResumabilityValue.Unknown, SeatVocabulary.ResumabilityReasonSessionIdMismatch, true)]
    [InlineData(ResumabilityValue.Lost, ResumabilityValue.Unknown, SeatVocabulary.ResumabilityReasonSessionIdMismatch, true)]
    [InlineData(ResumabilityValue.Unknown, ResumabilityValue.Unknown, "existing-unknown", true)]
    public void U7UnexpectedSessionObservationMarksMismatch(ResumabilityValue initial, ResumabilityValue expected,
        string? expectedReason, bool opensFinding)
    {
        var state = State(SessionValue.Present, initial);
        var step = Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new SessionObservedBody("foreign-session", false)));

        Assert.Equal(expected, step.State.Resumability);
        Assert.Equal(expectedReason, step.State.ResumabilityReason);
        Assert.Equal(opensFinding, step.Findings.Any(finding => finding.Kind == SeatVocabulary.FindingSessionIdMismatch && finding.Open));
        Assert.Equal(state.NativeSessionId, step.State.NativeSessionId);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(SessionValue.Absent, ResumabilityValue.None)]
    [InlineData(SessionValue.Starting, ResumabilityValue.FreshOnly)]
    [InlineData(SessionValue.Present, ResumabilityValue.Resumable)]
    [InlineData(SessionValue.Exited, ResumabilityValue.Lost)]
    [InlineData(SessionValue.Unknown, ResumabilityValue.Unknown)]
    public void U8RotationAdoptsNewSessionForEveryKnownSessionState(SessionValue session, ResumabilityValue resumability)
    {
        var state = State(session, resumability);
        if (session == SessionValue.Present)
        {
            state = state with { KnownActivity = ActivityValue.Working, Activity = ActivityValue.Working };
        }
        var input = HarnessReadinessTests.Harness(HarnessEventKind.SessionStarted, "native-2", RotationAttributes);
        var step = Apply(state, input);

        Assert.Equal("native-2", step.State.NativeSessionId);
        Assert.True(step.State.ReadinessSeen);
        Assert.Equal(state.KnownSession, step.State.KnownSession);
        Assert.Equal(session == SessionValue.Present ? ActivityValue.Idle : state.KnownActivity, step.State.KnownActivity);
        Assert.Equal(resumability == ResumabilityValue.None ? ResumabilityValue.None : ResumabilityValue.FreshOnly, step.State.Resumability);
        Assert.Contains(step.Effects, effect => effect == new AdoptRotatedSession("native-2", "native-1"));
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingSessionIdMismatch && !finding.Open);
        SeatAssert.Invariants(step);
    }

    [Theory]
    [InlineData(false, "native-2", "native-1")]
    [InlineData(true, "", "native-1")]
    [InlineData(true, "native-2", "not-native-1")]
    public void U7InvalidOrMismatchingRotationFallsBackToSessionIdMismatch(bool rotationProfile,
        string newNativeSessionId, string previousNativeSessionId)
    {
        var profile = rotationProfile ? Profile : new NoRotationProfile();
        var attributes = HarnessReadinessTests.Attrs(("source", "clear"), ("previous_session_id", previousNativeSessionId));
        var state = State(SessionValue.Present, ResumabilityValue.Resumable);
        var input = HarnessReadinessTests.Harness(HarnessEventKind.SessionStarted, newNativeSessionId, attributes);
        var step = SeatStateMachine.Apply(state, input, profile, Now);

        Assert.Equal(ResumabilityValue.Unknown, step.State.Resumability);
        Assert.Equal(SeatVocabulary.ResumabilityReasonSessionIdMismatch, step.State.ResumabilityReason);
        Assert.Equal("native-1", step.State.NativeSessionId);
        Assert.False(step.State.ReadinessSeen);
        Assert.DoesNotContain(step.Effects, effect => effect is AdoptRotatedSession);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingSessionIdMismatch && finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void NonrotationReadinessWithDifferentNativeIdUsesU7()
    {
        var state = State(SessionValue.Present, ResumabilityValue.Resumable);
        var input = HarnessReadinessTests.Harness(HarnessEventKind.SessionStarted, "native-2",
            HarnessReadinessTests.Attrs(("source", "startup")));
        var step = Apply(state, input);

        Assert.Equal(ResumabilityValue.Unknown, step.State.Resumability);
        Assert.Equal(SessionValue.Present, step.State.KnownSession);
        Assert.Equal(ActivityValue.Idle, step.State.KnownActivity);
        Assert.False(step.State.ReadinessSeen);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingSessionIdMismatch && finding.Open);
        SeatAssert.Invariants(step);
    }

    private static SeatStep Apply(SeatState state, SeatInput input) =>
        SeatStateMachine.Apply(state, input, Profile, Now);

    private static SeatState State(SessionValue session, ResumabilityValue resumability) =>
        HarnessReadinessTests.State(session) with
        {
            Resumability = resumability,
            ResumabilityReason = resumability == ResumabilityValue.Unknown ? "existing-unknown" : null,
            NativeSessionId = "native-1",
        };

    private sealed class NoRotationProfile : ClaudeLike
    {
        public override bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => false;
    }
}
