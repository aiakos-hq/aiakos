using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class PipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IHarnessStateProfile Profile = new ClaudeLike();

    [Fact]
    public void T8LateConversationEvidenceUpdatesResumabilityButNotActivity()
    {
        var state = State(ActivityValue.Idle) with { LastSourceSeq = 8 };
        var step = Apply(state, 1, 5, new HarnessBody(HarnessEventKind.PromptSubmitted, "native-1", EmptyAttributes));

        Assert.Equal(EventDisposition.Late, step.Disposition);
        Assert.Equal(8, step.State.LastSourceSeq);
        Assert.Equal(ResumabilityValue.Resumable, step.State.Resumability);
        Assert.Equal(ActivityValue.Idle, step.State.KnownActivity);
        Assert.Contains(step.Transitions, transition => transition.Rule == "U2");
        Assert.Contains(step.Findings, finding => finding.Kind == SeatVocabulary.FindingActivityStale && !finding.Open);
        Assert.DoesNotContain(step.Findings, finding => finding.Kind == SeatVocabulary.FindingDeliveryUnconfirmed);
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void T8LateRotationAdoptsNativeIdAndResumabilityWithoutChangingActivity()
    {
        var state = State(ActivityValue.Working) with
        {
            KnownActivityDetail = "tool:search",
            ActivityDetail = "tool:search",
            LastSourceSeq = 8,
        };
        var attributes = new Dictionary<string, string>
        {
            ["source"] = "clear",
            ["previous_session_id"] = "native-1",
        };
        var step = Apply(state, 1, 5, new HarnessBody(HarnessEventKind.SessionStarted, "native-2", attributes));

        Assert.Equal(EventDisposition.Late, step.Disposition);
        Assert.Equal(8, step.State.LastSourceSeq);
        Assert.Equal("native-2", step.State.NativeSessionId);
        Assert.Equal(ResumabilityValue.FreshOnly, step.State.Resumability);
        Assert.Equal(ActivityValue.Working, step.State.KnownActivity);
        Assert.Equal("tool:search", step.State.KnownActivityDetail);
        Assert.Contains(new AdoptRotatedSession("native-2", "native-1"), step.Effects);
        Assert.DoesNotContain(step.Transitions, transition => transition.Rule == "A1");
        SeatAssert.Invariants(step);
    }

    [Fact]
    public void T8TelemetryDoesNotAdvanceSourceWatermarkOrMakeEarlierTurnEndLate()
    {
        var state = State(ActivityValue.Working);
        var telemetry = Apply(state, 1, 50,
            new HarnessBody(HarnessEventKind.Telemetry, "native-1", EmptyAttributes));
        Assert.Equal(EventDisposition.Applied, telemetry.Disposition);
        Assert.Equal(0, telemetry.State.LastSourceSeq);
        Assert.Equal(ActivityValue.Working, telemetry.State.KnownActivity);

        var turnEnded = Apply(telemetry.State, 2, 2,
            new HarnessBody(HarnessEventKind.TurnEnded, "native-1", EmptyAttributes));
        Assert.Equal(EventDisposition.Applied, turnEnded.Disposition);
        Assert.Equal(2, turnEnded.State.LastSourceSeq);
        Assert.Equal(ActivityValue.Idle, turnEnded.State.KnownActivity);
        Assert.Contains(turnEnded.Transitions, transition => transition.Rule == "A10");
        SeatAssert.Invariants(turnEnded);
    }

    [Fact]
    public void T8PipelineAppliesLaunchAttributionBeforeOrphanAndLateGuards()
    {
        var state = State(ActivityValue.Idle);
        var staleLaunch = SeatStateMachine.Apply(state,
            new EventReceived(NodeId, state.NextSeq, 1, Guid.Empty,
                new HarnessBody(HarnessEventKind.SessionEnded, "native-1", EmptyAttributes)), Profile, Now);
        Assert.Equal(EventDisposition.StaleLaunch, staleLaunch.Disposition);
        Assert.Equal(0, staleLaunch.State.LastSourceSeq);

        var orphan = Apply(state, 1, 1,
            new HarnessBody(HarnessEventKind.SessionEnded, "native-1", EmptyAttributes));
        Assert.Equal(EventDisposition.Orphan, orphan.Disposition);
        Assert.Equal(0, orphan.State.LastSourceSeq);
        Assert.Equal(state.KnownSession, orphan.State.KnownSession);
        Assert.Contains(orphan.Findings, finding => finding.Kind == SeatVocabulary.FindingActivityStale && !finding.Open);
        SeatAssert.Invariants(orphan);
    }

    [Fact]
    public void T8IncreasingSourceSequenceAppliesActivityAndAdvancesWatermark()
    {
        var state = State(ActivityValue.Idle) with { LastSourceSeq = 3 };
        var step = Apply(state, 1, 4,
            new HarnessBody(HarnessEventKind.PromptSubmitted, "native-1", EmptyAttributes));

        Assert.Equal(EventDisposition.Applied, step.Disposition);
        Assert.Equal(4, step.State.LastSourceSeq);
        Assert.Equal(ActivityValue.Working, step.State.KnownActivity);
        Assert.Equal(ResumabilityValue.Resumable, step.State.Resumability);
        SeatAssert.Invariants(step);
    }

    private static IReadOnlyDictionary<string, string> EmptyAttributes { get; } = new Dictionary<string, string>();

    private static SeatStep Apply(SeatState state, long seq, long sourceSeq, HarnessBody body) =>
        SeatStateMachine.Apply(state, new EventReceived(NodeId, seq, sourceSeq, LaunchId, body), Profile, Now);

    private static SeatState State(ActivityValue activity) => SeatState.Initial(Now) with
    {
        Session = SessionValue.Present,
        KnownSession = SessionValue.Present,
        Activity = activity,
        KnownActivity = activity,
        ActivitySince = Now.AddMinutes(-1),
        ActivityDetail = activity == ActivityValue.Working ? "tool:search" : null,
        KnownActivityDetail = activity == ActivityValue.Working ? "tool:search" : null,
        Resumability = ResumabilityValue.FreshOnly,
        Desired = SeatDesired.Up,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NativeSessionId = "native-1",
        NodeInstanceId = NodeId,
    };
}
