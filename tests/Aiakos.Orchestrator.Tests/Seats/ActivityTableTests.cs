using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class ActivityTableTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IHarnessStateProfile Profile = new ClaudeLike();

    [Fact]
    public void A2PromptSubmittedMovesAnyApplicableActivityToWorkingAndClearsPendingInput()
    {
        AssertActivity(HarnessEventKind.PromptSubmitted, ActivityValue.Idle, ActivityValue.Working);
        AssertActivity(HarnessEventKind.PromptSubmitted, ActivityValue.Working, ActivityValue.Working);
        AssertActivity(HarnessEventKind.PromptSubmitted, ActivityValue.NeedsInput, ActivityValue.Working, "request");
        AssertActivity(HarnessEventKind.PromptSubmitted, ActivityValue.Unknown, ActivityValue.Working);
    }

    [Fact]
    public void A3ActiveLevelMovesIdleAndUnknownToWorkingButLeavesNeedsInputSticky()
    {
        AssertActivity(HarnessEventKind.Active, ActivityValue.Idle, ActivityValue.Working);
        AssertActivity(HarnessEventKind.Active, ActivityValue.Working, ActivityValue.Working);
        AssertActivity(HarnessEventKind.Active, ActivityValue.NeedsInput, ActivityValue.NeedsInput, "request");
        AssertActivity(HarnessEventKind.Active, ActivityValue.Unknown, ActivityValue.Working);
    }

    [Fact]
    public void A4ToolStartedSetsWorkingDetailExceptWhileNeedsInputIsSticky()
    {
        AssertActivity(HarnessEventKind.ToolStarted, ActivityValue.Idle, ActivityValue.Working, detail: "tool:search");
        AssertActivity(HarnessEventKind.ToolStarted, ActivityValue.Working, ActivityValue.Working, detail: "tool:search");
        AssertActivity(HarnessEventKind.ToolStarted, ActivityValue.NeedsInput, ActivityValue.NeedsInput, "request");
        AssertActivity(HarnessEventKind.ToolStarted, ActivityValue.Unknown, ActivityValue.Working, detail: "tool:search");
    }

    [Fact]
    public void A5ToolFinishedMovesApplicableStatesToWorkingAndOnlyResolvesMatchingInput()
    {
        AssertActivity(HarnessEventKind.ToolFinished, ActivityValue.Idle, ActivityValue.Working);
        var finished = AssertActivity(HarnessEventKind.ToolFinished, ActivityValue.Working, ActivityValue.Working, detail: "tool:old");
        Assert.Null(finished.State.KnownActivityDetail);
        AssertActivity(HarnessEventKind.ToolFinished, ActivityValue.NeedsInput, ActivityValue.Working, "tool-1",
            new Dictionary<string, string> { ["tool_use_id"] = "tool-1" });
        AssertActivity(HarnessEventKind.ToolFinished, ActivityValue.NeedsInput, ActivityValue.NeedsInput, "tool-1",
            new Dictionary<string, string> { ["tool_use_id"] = "tool-2" });
        AssertActivity(HarnessEventKind.ToolFinished, ActivityValue.Unknown, ActivityValue.Working);
    }

    [Fact]
    public void A6InputRequestedStoresRequestIdOrWildcardAndSetsNeedsInput()
    {
        AssertInputRequested(ActivityValue.Idle, new Dictionary<string, string> { ["request_id"] = "request-1" }, "request-1");
        AssertInputRequested(ActivityValue.Working, new Dictionary<string, string>(), "*");
        AssertInputRequested(ActivityValue.NeedsInput, new Dictionary<string, string> { ["request_id"] = "request-2" }, "request-2");
        AssertInputRequested(ActivityValue.Unknown, new Dictionary<string, string>(), "*");
    }

    [Fact]
    public void A7InputResolvedChangesNeedsInputOnlyWhenItMatchesAndMovesUnknownToWorking()
    {
        AssertActivity(HarnessEventKind.InputResolved, ActivityValue.Idle, ActivityValue.Idle, pending: "request");
        AssertActivity(HarnessEventKind.InputResolved, ActivityValue.Working, ActivityValue.Working, pending: "request");
        AssertActivity(HarnessEventKind.InputResolved, ActivityValue.NeedsInput, ActivityValue.Working, "request",
            new Dictionary<string, string> { ["request_id"] = "request" });
        AssertActivity(HarnessEventKind.InputResolved, ActivityValue.NeedsInput, ActivityValue.NeedsInput, "request",
            new Dictionary<string, string> { ["request_id"] = "other" });
        AssertActivity(HarnessEventKind.InputResolved, ActivityValue.Unknown, ActivityValue.Working, pending: "request");
    }

    [Fact]
    public void GS8ParallelToolFinishesResolveWildcardInputButKeepIdentifiedInputOpen()
    {
        var wildcard = State(ActivityValue.Idle);
        wildcard = Apply(wildcard, HarnessEventKind.ToolStarted, new Dictionary<string, string> { ["tool_name"] = "first" }).State;
        wildcard = Apply(wildcard, HarnessEventKind.ToolStarted, new Dictionary<string, string> { ["tool_name"] = "second" }).State;
        wildcard = Apply(wildcard, HarnessEventKind.InputRequested, new Dictionary<string, string>()).State;
        var wildcardResult = Apply(wildcard, HarnessEventKind.ToolFinished, new Dictionary<string, string> { ["tool_use_id"] = "second" });
        Assert.Equal(ActivityValue.Working, wildcardResult.State.KnownActivity);
        Assert.Null(wildcardResult.State.PendingInputRequest);
        SeatAssert.Invariants(wildcardResult);

        var identified = Apply(State(ActivityValue.Idle), HarnessEventKind.ToolStarted, new Dictionary<string, string>()).State;
        identified = Apply(identified, HarnessEventKind.InputRequested, new Dictionary<string, string> { ["request_id"] = "first" }).State;
        var identifiedResult = Apply(identified, HarnessEventKind.ToolFinished, new Dictionary<string, string> { ["tool_use_id"] = "second" });
        Assert.Equal(ActivityValue.NeedsInput, identifiedResult.State.KnownActivity);
        Assert.Equal("first", identifiedResult.State.PendingInputRequest);
        SeatAssert.Invariants(identifiedResult);
    }

    [Fact]
    public void ActivityChangesUnderOverlayUpdateKnownStateAndKeepReportedState()
    {
        var state = State(ActivityValue.Idle) with
        {
            Overlay = SeatOverlay.NodeLinkLost,
            Activity = ActivityValue.Unknown,
            ActivityReason = SeatVocabulary.SessionReasonNodeLinkLost,
        };

        var step = Apply(state, HarnessEventKind.PromptSubmitted, new Dictionary<string, string>());

        Assert.Equal(ActivityValue.Working, step.State.KnownActivity);
        Assert.Equal(ActivityValue.Unknown, step.State.Activity);
        Assert.Equal(SeatVocabulary.SessionReasonNodeLinkLost, step.State.ActivityReason);
        Assert.Contains(step.Transitions, transition => transition.Axis == "activity" && !transition.Reported && transition.Rule == "A2");
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingActivityStale && !finding.Open);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void ActivityEventsWhileStartingOrSessionUnknownAreAppliedWithoutChangingActivity()
    {
        foreach (var session in new[] { SessionValue.Starting, SessionValue.Unknown })
        {
            var reason = session == SessionValue.Starting ? "starting" : "unknown";
            var activityReason = session == SessionValue.Starting
                ? SeatVocabulary.ActivityReasonNotReady
                : SeatVocabulary.ActivityReasonSessionUnknown;
            var state = State(ActivityValue.Unknown) with
            {
                Session = session,
                KnownSession = session,
                SessionReason = reason,
                KnownSessionReason = reason,
                ActivityReason = activityReason,
                KnownActivityReason = activityReason,
            };

            var step = Apply(state, HarnessEventKind.PromptSubmitted, new Dictionary<string, string>());

            Assert.Equal(ActivityValue.Unknown, step.State.KnownActivity);
            Assert.Equal(EventDisposition.Applied, step.Disposition);
            SeatAssert.Invariants(step);
        }
    }

    [Fact]
    public void ASessionChangeClearsPendingInputRequest()
    {
        var state = State(ActivityValue.NeedsInput, "request-1");
        var step = SeatStateMachine.Apply(state,
            new EventReceived(NodeId, 1, 0, LaunchId, new ProcessExitedBody(null, null)), Profile, Now);

        Assert.Equal(SessionValue.Exited, step.State.KnownSession);
        Assert.Null(step.State.PendingInputRequest);
        SeatAssert.Invariants(step);
    }

    private static SeatStep AssertActivity(HarnessEventKind kind, ActivityValue initial, ActivityValue expected,
        string? pending = null, Dictionary<string, string>? attributes = null, string? detail = null)
    {
        var step = Apply(State(initial, pending), kind,
            attributes ?? new Dictionary<string, string> { ["tool_name"] = "search" });
        Assert.Equal(expected, step.State.KnownActivity);
        var expectedPending = pending;
        if (kind == HarnessEventKind.PromptSubmitted)
            expectedPending = null;
        else if (kind == HarnessEventKind.InputRequested)
            expectedPending = attributes is not null && attributes.TryGetValue("request_id", out var requestId) ? requestId : "*";
        else if (kind is HarnessEventKind.ToolFinished or HarnessEventKind.InputResolved)
        {
            var matches = pending == "*" || attributes is not null &&
                (attributes.TryGetValue("tool_use_id", out var toolId) && toolId == pending ||
                 attributes.TryGetValue("request_id", out var requestId) && requestId == pending);
            if (matches) expectedPending = null;
        }
        Assert.Equal(expectedPending, step.State.PendingInputRequest);
        if (detail is not null && kind != HarnessEventKind.ToolFinished) Assert.Equal(detail, step.State.KnownActivityDetail);
        if (initial != expected)
        {
            var expectedRule = kind switch
            {
                HarnessEventKind.PromptSubmitted => "A2",
                HarnessEventKind.Active => "A3",
                HarnessEventKind.ToolStarted => "A4",
                HarnessEventKind.ToolFinished => "A5",
                HarnessEventKind.InputRequested => "A6",
                _ => "A7"
            };
            Assert.Contains(step.Transitions, transition => transition.Axis == "activity" && transition.Rule == expectedRule);
            Assert.Equal(Now, step.State.ActivitySince);
        }
        else
        {
            Assert.DoesNotContain(step.Transitions, transition => transition.Axis == "activity");
            Assert.Equal(Now.AddMinutes(-1), step.State.ActivitySince);
        }
        Assert.Equal(Now, step.State.LastEventAt);
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingActivityStale && !finding.Open);
        Assert.Equal(EventDisposition.Applied, step.Disposition);
        SeatAssert.Invariants(step);
        return step;
    }

    private static void AssertInputRequested(ActivityValue initial, IReadOnlyDictionary<string, string> attributes, string expectedPending)
    {
        var step = Apply(State(initial), HarnessEventKind.InputRequested, attributes);
        Assert.Equal(ActivityValue.NeedsInput, step.State.KnownActivity);
        Assert.Equal(expectedPending, step.State.PendingInputRequest);
        Assert.Equal(EventDisposition.Applied, step.Disposition);
        SeatAssert.Invariants(step);
    }

    private static SeatStep Apply(SeatState state, HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        SeatStateMachine.Apply(state, new EventReceived(NodeId, 1, 0, LaunchId,
            new HarnessBody(kind, "native-1", attributes)), Profile, Now);

    private static SeatState State(ActivityValue activity, string? pending = null) => SeatState.Initial(Now) with
    {
        Session = SessionValue.Present,
        KnownSession = SessionValue.Present,
        Activity = activity,
        KnownActivity = activity,
        ActivitySince = Now.AddMinutes(-1),
        ActivityDetail = activity == ActivityValue.Working ? "tool:old" : null,
        KnownActivityDetail = activity == ActivityValue.Working ? "tool:old" : null,
        ActivityReason = activity == ActivityValue.Unknown ? "test" : null,
        KnownActivityReason = activity == ActivityValue.Unknown ? "test" : null,
        Resumability = ResumabilityValue.Resumable,
        Desired = SeatDesired.Up,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NativeSessionId = "native-1",
        PendingInputRequest = pending,
        NodeInstanceId = NodeId
    };
}
