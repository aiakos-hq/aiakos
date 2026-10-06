using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class GoldenScriptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void GS1PermissionTurnMovesThroughReadinessToolsInputAndCompaction()
    {
        var profile = new ClaudeLike();
        var state = SeatState.Initial(Now) with
        {
            Session = SessionValue.Starting,
            KnownSession = SessionValue.Starting,
            SessionReason = null,
            KnownSessionReason = null,
            Activity = ActivityValue.Unknown,
            KnownActivity = ActivityValue.Unknown,
            ActivityReason = SeatVocabulary.ActivityReasonNotReady,
            KnownActivityReason = SeatVocabulary.ActivityReasonNotReady,
            Resumability = ResumabilityValue.FreshOnly,
            Desired = SeatDesired.Up,
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
            NativeSessionId = "native-1",
            NodeInstanceId = NodeId,
        };

        var script = new (HarnessEventKind Kind, IReadOnlyDictionary<string, string> Attributes,
            SessionValue Session, ActivityValue Activity, string? Detail, ResumabilityValue Resumability)[]
        {
            (HarnessEventKind.SessionStarted, new Dictionary<string, string> { ["source"] = "startup" }, SessionValue.Present, ActivityValue.Idle, null, ResumabilityValue.FreshOnly),
            (HarnessEventKind.PromptSubmitted, new Dictionary<string, string>(), SessionValue.Present, ActivityValue.Working, null, ResumabilityValue.Resumable),
            (HarnessEventKind.ToolStarted, new Dictionary<string, string> { ["tool_name"] = "Bash", ["tool_use_id"] = "t1" }, SessionValue.Present, ActivityValue.Working, "tool:Bash", ResumabilityValue.Resumable),
            (HarnessEventKind.InputRequested, new Dictionary<string, string> { ["request_id"] = "t1" }, SessionValue.Present, ActivityValue.NeedsInput, null, ResumabilityValue.Resumable),
            (HarnessEventKind.Other, new Dictionary<string, string>(), SessionValue.Present, ActivityValue.NeedsInput, null, ResumabilityValue.Resumable),
            (HarnessEventKind.ToolFinished, new Dictionary<string, string> { ["tool_use_id"] = "t1" }, SessionValue.Present, ActivityValue.Working, null, ResumabilityValue.Resumable),
            (HarnessEventKind.TurnEnded, new Dictionary<string, string>(), SessionValue.Present, ActivityValue.Idle, null, ResumabilityValue.Resumable),
            (HarnessEventKind.CompactionStarted, new Dictionary<string, string>(), SessionValue.Present, ActivityValue.Working, SeatVocabulary.ActivityDetailCompacting, ResumabilityValue.Resumable),
            (HarnessEventKind.Compacted, new Dictionary<string, string>(), SessionValue.Present, ActivityValue.Idle, null, ResumabilityValue.Resumable),
        };

        long sourceSeq = 0;
        foreach (var entry in script)
        {
            var step = SeatStateMachine.Apply(state,
                new EventReceived(NodeId, state.NextSeq, sourceSeq++, LaunchId,
                    new HarnessBody(entry.Kind, "native-1", entry.Attributes)), profile, Now);
            Assert.Equal(entry.Session, step.State.KnownSession);
            Assert.Equal(entry.Activity, step.State.KnownActivity);
            Assert.Equal(entry.Detail, step.State.KnownActivityDetail);
            Assert.Equal(entry.Resumability, step.State.Resumability);
            SeatAssert.Invariants(step);
            state = step.State;
        }
    }

    [Fact]
    public void GS4TurnFailureResolvesDeliveryUnconfirmedAndNextTurnEndResolvesTurnFailure()
    {
        var state = State(ActivityValue.Idle);

        var prompt = Apply(state, HarnessEventKind.PromptSubmitted, 0, new ClaudeLike());
        AssertAxes(prompt, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
        Assert.Contains(prompt.Findings, finding => finding.Kind == SeatVocabulary.FindingDeliveryUnconfirmed && !finding.Open);
        state = prompt.State;

        var failed = Apply(state, HarnessEventKind.TurnFailed, 1, new ClaudeLike());
        AssertAxes(failed, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
        Assert.Contains(failed.Findings, finding => finding.Kind == SeatVocabulary.FindingTurnFailed && finding.Open);
        state = failed.State;

        var ended = Apply(state, HarnessEventKind.TurnEnded, 2, new ClaudeLike());
        AssertAxes(ended, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
        Assert.Contains(ended.Findings, finding => finding.Kind == SeatVocabulary.FindingTurnFailed && !finding.Open);
    }

    [Fact]
    public void GS5ProcessExitWithoutSessionEndedMovesWorkingSeatToExited()
    {
        var state = State(ActivityValue.Working);
        var step = SeatStateMachine.Apply(state,
            new EventReceived(NodeId, state.NextSeq, 0, LaunchId, new ProcessExitedBody(3, null)),
            new ClaudeLike(), Now);

        AssertAxes(step, SessionValue.Exited, ActivityValue.None, ResumabilityValue.Resumable);
        Assert.Contains(step.Transitions, transition => transition.Axis == "session" && transition.Rule == "S14");
        Assert.Contains(step.Transitions, transition => transition.Axis == "activity" && transition.Rule == "R10");
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingUnexpectedExit && finding.Open);
    }

    [Fact]
    public void GS2KillAndResumeReconcilesEarlyEndReadinessAndLaunchResult()
    {
        var profile = new ClaudeLike();
        var state = State(ActivityValue.Idle) with { Resumability = ResumabilityValue.FreshOnly };

        var prompt = Apply(state, HarnessEventKind.PromptSubmitted, 0, profile);
        AssertAxes(prompt, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
        state = prompt.State;
        var ended = Apply(state, HarnessEventKind.TurnEnded, 0, profile);
        AssertAxes(ended, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
        state = ended.State;

        var exited = ApplyBody(state, new ProcessExitedBody(null, 9), profile);
        AssertAxes(exited, SessionValue.Exited, ActivityValue.None, ResumabilityValue.Resumable);
        Assert.Contains(exited.Findings,
            finding => finding.Kind == SeatVocabulary.FindingUnexpectedExit && finding.Open);
        state = exited.State;

        var up = SeatStateMachine.Apply(state,
            new UpRequested(false, true, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "ignored"),
            profile, Now);
        var resume = Assert.IsType<StartLaunch>(Assert.Single(up.Effects));
        Assert.Equal(LaunchMode.Resume, resume.Mode);
        Assert.Equal(SeatVocabulary.DecisionResume, resume.Decision);
        Assert.Equal("native-1", resume.NativeSessionId);
        Assert.Equal(SessionValue.Starting, up.State.KnownSession);
        Assert.Equal(ActivityValue.Unknown, up.State.KnownActivity);
        Assert.Equal(ResumabilityValue.Resumable, up.State.Resumability);
        SeatAssert.Invariants(up);
        state = up.State;

        var orphanEnd = Apply(state, HarnessEventKind.SessionEnded, 0, profile);
        Assert.Equal(EventDisposition.Orphan, orphanEnd.Disposition);
        Assert.Equal(SessionValue.Starting, orphanEnd.State.KnownSession);
        Assert.Equal(ActivityValue.Unknown, orphanEnd.State.KnownActivity);
        SeatAssert.Invariants(orphanEnd);
        state = orphanEnd.State;

        var ready = Apply(state, HarnessEventKind.SessionStarted, 0, profile,
            new Dictionary<string, string> { ["source"] = "resume" });
        AssertAxes(ready, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
        state = ready.State;

        var launchReady = ApplyBody(state, new LaunchResultBody(LaunchOutcome.Ready, "", null), profile);
        AssertAxes(launchReady, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
    }

    [Fact]
    public void GS3FailedResumeRequiresExplicitFreshLaunch()
    {
        var profile = new ClaudeLike();
        var state = State(ActivityValue.Idle) with { Resumability = ResumabilityValue.FreshOnly };
        var prompt = Apply(state, HarnessEventKind.PromptSubmitted, 0, profile);
        AssertAxes(prompt, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
        state = prompt.State;
        var ended = Apply(state, HarnessEventKind.TurnEnded, 0, profile);
        AssertAxes(ended, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
        state = ended.State;
        var exited = ApplyBody(state, new ProcessExitedBody(1, null), profile);
        AssertAxes(exited, SessionValue.Exited, ActivityValue.None, ResumabilityValue.Resumable);
        Assert.Contains(exited.Findings,
            finding => finding.Kind == SeatVocabulary.FindingUnexpectedExit && finding.Open);
        state = exited.State;

        var resumeUp = SeatStateMachine.Apply(state,
            new UpRequested(false, true, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "ignored"),
            profile, Now);
        var start = Assert.IsType<StartLaunch>(Assert.Single(resumeUp.Effects));
        Assert.Equal(LaunchMode.Resume, start.Mode);
        Assert.Equal(SeatVocabulary.DecisionResume, start.Decision);
        Assert.Equal(SessionValue.Starting, resumeUp.State.KnownSession);
        Assert.Equal(ActivityValue.Unknown, resumeUp.State.KnownActivity);
        Assert.Equal(ResumabilityValue.Resumable, resumeUp.State.Resumability);
        SeatAssert.Invariants(resumeUp);
        state = resumeUp.State;

        var failed = ApplyBody(state,
            new LaunchResultBody(LaunchOutcome.Failed, SeatVocabulary.LaunchReasonResumeSessionNotFound, 1), profile);
        Assert.Equal(SessionValue.Exited, failed.State.KnownSession);
        Assert.Equal(ActivityValue.None, failed.State.KnownActivity);
        Assert.Equal(ResumabilityValue.Lost, failed.State.Resumability);
        Assert.Equal(EventDisposition.Applied, failed.Disposition);
        Assert.Equal(Now, failed.State.LastEventAt);
        Assert.Contains(failed.Findings, finding => finding.Kind == SeatVocabulary.FindingResumeLost && finding.Open);
        Assert.Empty(failed.Effects);
        SeatAssert.Invariants(failed);
        state = failed.State;

        var rejected = SeatStateMachine.Apply(state,
            new UpRequested(false, true, Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "ignored"),
            profile, Now);
        Assert.Equal(new Rejected(SeatVocabulary.RejectionResumeLost), rejected.Reply);
        Assert.Equal(state, rejected.State);
        Assert.Empty(rejected.Effects);
        SeatAssert.Invariants(rejected);

        var fresh = SeatStateMachine.Apply(state,
            new UpRequested(true, true, Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "native-new"),
            profile, Now);
        var freshStart = Assert.IsType<StartLaunch>(Assert.Single(fresh.Effects));
        Assert.Equal(LaunchMode.Fresh, freshStart.Mode);
        Assert.Equal(SeatVocabulary.DecisionFreshExplicit, freshStart.Decision);
        Assert.True(freshStart.NewSession);
        Assert.True(freshStart.AbandonPreviousSession);
        Assert.Equal(SessionValue.Starting, fresh.State.KnownSession);
        Assert.Equal(ActivityValue.Unknown, fresh.State.KnownActivity);
        Assert.Equal(ResumabilityValue.FreshOnly, fresh.State.Resumability);
        SeatAssert.Invariants(fresh);
    }

    [Fact]
    public void GS6ClearRotationAdoptsNativeIdAndResumesWithIt()
    {
        var profile = new ClaudeLike();
        var state = State(ActivityValue.Idle);
        var telemetry = Apply(state, HarnessEventKind.Other, 0, profile,
            new Dictionary<string, string> { ["reason"] = "clear" });
        AssertAxes(telemetry, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
        state = telemetry.State;

        var rotated = Apply(state, HarnessEventKind.SessionStarted, 0, profile,
            new Dictionary<string, string>
            {
                ["source"] = "clear",
                ["previous_session_id"] = "native-1"
            }, nativeSessionId: "native-2");
        Assert.Equal(SessionValue.Present, rotated.State.KnownSession);
        Assert.Equal(ActivityValue.Idle, rotated.State.KnownActivity);
        Assert.Equal(ResumabilityValue.FreshOnly, rotated.State.Resumability);
        Assert.Equal(EventDisposition.Applied, rotated.Disposition);
        Assert.Equal(Now, rotated.State.LastEventAt);
        SeatAssert.Invariants(rotated);
        Assert.Equal("native-2", rotated.State.NativeSessionId);
        Assert.Equal(new SeatEffect[] { new AdoptRotatedSession("native-2", "native-1") }, rotated.Effects);
        Assert.Contains(rotated.Findings,
            finding => finding.Kind == SeatVocabulary.FindingSessionIdMismatch && !finding.Open);
        state = rotated.State;

        var prompt = Apply(state, HarnessEventKind.PromptSubmitted, 0, profile, nativeSessionId: "native-2");
        AssertAxes(prompt, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
        state = prompt.State;

        var down = SeatStateMachine.Apply(state, new DownRequested(), profile, Now);
        Assert.IsType<Accepted>(down.Reply);
        Assert.Equal(new SeatEffect[] { new DispatchStop(LaunchId) }, down.Effects);
        Assert.Equal(SessionValue.Present, down.State.KnownSession);
        Assert.Equal(ActivityValue.Working, down.State.KnownActivity);
        Assert.Equal(ResumabilityValue.Resumable, down.State.Resumability);
        SeatAssert.Invariants(down);
        state = down.State;
        var stopped = ApplyBody(state, new StopResultBody(StopOutcome.Stopped), profile);
        AssertAxes(stopped, SessionValue.Absent, ActivityValue.None, ResumabilityValue.Resumable);
        state = stopped.State;

        var up = SeatStateMachine.Apply(state,
            new UpRequested(false, true, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "ignored"),
            profile, Now);
        var resume = Assert.IsType<StartLaunch>(Assert.Single(up.Effects));
        Assert.Equal(LaunchMode.Resume, resume.Mode);
        Assert.Equal("native-2", resume.NativeSessionId);
        Assert.False(resume.NewSession);
        SeatAssert.Invariants(up);

        var invalidRotationState = State(ActivityValue.Idle);
        var invalid = Apply(invalidRotationState, HarnessEventKind.SessionStarted, 0, profile,
            new Dictionary<string, string>
            {
                ["source"] = "clear",
                ["previous_session_id"] = "native-1"
            }, nativeSessionId: " ");
        Assert.Equal("native-1", invalid.State.NativeSessionId);
        Assert.Equal(ResumabilityValue.Unknown, invalid.State.Resumability);
        Assert.DoesNotContain(invalid.Effects, effect => effect is AdoptRotatedSession);
        Assert.Contains(invalid.Findings,
            finding => finding.Kind == SeatVocabulary.FindingSessionIdMismatch && finding.Open);
        SeatAssert.Invariants(invalid);
    }

    [Fact]
    public void GS7NeedsInputDeniesNormalAndForcedSendUntilPromptSubmission()
    {
        var profile = new ClaudeLike();
        var state = State(ActivityValue.NeedsInput);

        var send = SeatStateMachine.Apply(state, new SendRequested(false, true, false), profile, Now);
        Assert.Equal(new Rejected("SEAT_NEEDS_INPUT"), send.Reply);
        Assert.Equal(state, send.State);
        SeatAssert.Invariants(send);

        var forced = SeatStateMachine.Apply(state, new SendRequested(true, true, false), profile, Now);
        Assert.Equal(new Rejected("SEAT_NEEDS_INPUT"), forced.Reply);
        Assert.Equal(state, forced.State);
        SeatAssert.Invariants(forced);

        var prompt = Apply(state, HarnessEventKind.PromptSubmitted, 0, profile);
        AssertAxes(prompt, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
    }

    [Fact]
    public void GS9OpenCodeInputRequestRemainsStickyUntilResolvedAndTurnEndsIdle()
    {
        var profile = new OpenCodeLike();
        var state = State(ActivityValue.Idle);

        var active = Apply(state, HarnessEventKind.Active, 0, profile);
        AssertAxes(active, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
        state = active.State;

        var requested = Apply(state, HarnessEventKind.InputRequested, 1, profile,
            new Dictionary<string, string> { ["request_id"] = "p1" });
        AssertAxes(requested, SessionValue.Present, ActivityValue.NeedsInput, ResumabilityValue.Resumable);
        Assert.Equal("p1", requested.State.PendingInputRequest);
        state = requested.State;

        var stillRequested = Apply(state, HarnessEventKind.Active, 2, profile);
        AssertAxes(stillRequested, SessionValue.Present, ActivityValue.NeedsInput, ResumabilityValue.Resumable);
        Assert.Equal("p1", stillRequested.State.PendingInputRequest);
        state = stillRequested.State;

        var resolved = Apply(state, HarnessEventKind.InputResolved, 3, profile,
            new Dictionary<string, string> { ["request_id"] = "p1" });
        AssertAxes(resolved, SessionValue.Present, ActivityValue.Working, ResumabilityValue.Resumable);
        Assert.Null(resolved.State.PendingInputRequest);
        state = resolved.State;

        var ended = Apply(state, HarnessEventKind.TurnEnded, 4, profile);
        AssertAxes(ended, SessionValue.Present, ActivityValue.Idle, ResumabilityValue.Resumable);
    }

    private static SeatStep Apply(SeatState state, HarnessEventKind kind, long sourceSeq,
        IHarnessStateProfile profile, IReadOnlyDictionary<string, string>? attributes = null,
        string nativeSessionId = "native-1") =>
        SeatStateMachine.Apply(state,
            new EventReceived(NodeId, state.NextSeq, sourceSeq, state.Launch!.LaunchId,
                new HarnessBody(kind, nativeSessionId, attributes ?? new Dictionary<string, string>())),
            profile, Now);

    private static SeatStep ApplyBody(SeatState state, SeatEventBody body, IHarnessStateProfile profile) =>
        SeatStateMachine.Apply(state,
            new EventReceived(NodeId, state.NextSeq, 0, state.Launch!.LaunchId, body), profile, Now);

    private static void AssertAxes(SeatStep step, SessionValue session, ActivityValue activity,
        ResumabilityValue resumability)
    {
        Assert.Equal(session, step.State.KnownSession);
        Assert.Equal(activity, step.State.KnownActivity);
        Assert.Equal(resumability, step.State.Resumability);
        Assert.Equal(EventDisposition.Applied, step.Disposition);
        Assert.Equal(Now, step.State.LastEventAt);
        Assert.Empty(step.Effects);
        SeatAssert.Invariants(step);
    }

    private static SeatState State(ActivityValue activity) => SeatState.Initial(Now) with
    {
        Session = SessionValue.Present,
        KnownSession = SessionValue.Present,
        Activity = activity,
        KnownActivity = activity,
        ActivitySince = Now.AddMinutes(-1),
        ActivityDetail = activity == ActivityValue.Working ? "tool:search" : null,
        KnownActivityDetail = activity == ActivityValue.Working ? "tool:search" : null,
        Resumability = ResumabilityValue.Resumable,
        Desired = SeatDesired.Up,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NativeSessionId = "native-1",
        NodeInstanceId = NodeId
    };
}
