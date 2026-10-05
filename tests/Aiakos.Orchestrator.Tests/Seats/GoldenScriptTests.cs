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
        IHarnessStateProfile profile, IReadOnlyDictionary<string, string>? attributes = null) =>
        SeatStateMachine.Apply(state,
            new EventReceived(NodeId, state.NextSeq, sourceSeq, LaunchId,
                new HarnessBody(kind, "native-1", attributes ?? new Dictionary<string, string>())),
            profile, Now);

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
