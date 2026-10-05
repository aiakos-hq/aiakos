using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class KnownLimitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void T12PinnedOutOfOrderCompactionCanLoseHistoryMidTurn()
    {
        // A late COMPACTION_STARTED is ignored after COMPACTED arrived first, so the earlier idle state remains.
        var sourceEvents = new[]
        {
            new HarnessBody(HarnessEventKind.PromptSubmitted, "native-1", EmptyAttributes),
            new HarnessBody(HarnessEventKind.CompactionStarted, "native-1", EmptyAttributes),
            new HarnessBody(HarnessEventKind.Compacted, "native-1", EmptyAttributes),
        };

        var arrival = ApplyOrder(sourceEvents, [2, 0, 1]);
        var sourceOrder = ApplyOrder(sourceEvents, [0, 1, 2]);

        Assert.Equal(ActivityValue.Idle, arrival.KnownActivity);
        Assert.Equal(ActivityValue.Working, sourceOrder.KnownActivity);
    }

    private static SeatState ApplyOrder(HarnessBody[] sourceEvents, int[] indexes)
    {
        var state = InitialState();
        for (var transportSeq = 0; transportSeq < indexes.Length; transportSeq++)
        {
            var sourceIndex = indexes[transportSeq];
            var step = SeatStateMachine.Apply(state,
                new EventReceived(NodeId, state.NextSeq, sourceIndex + 1, LaunchId, sourceEvents[sourceIndex]),
                new ClaudeLike(), Now);
            SeatAssert.Invariants(step);
            state = step.State;
        }

        return state;
    }

    private static IReadOnlyDictionary<string, string> EmptyAttributes { get; } = new Dictionary<string, string>();

    private static SeatState InitialState() => SeatState.Initial(Now) with
    {
        Session = SessionValue.Present,
        KnownSession = SessionValue.Present,
        Activity = ActivityValue.Idle,
        KnownActivity = ActivityValue.Idle,
        Resumability = ResumabilityValue.FreshOnly,
        Desired = SeatDesired.Up,
        Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
        NativeSessionId = "native-1",
        NodeInstanceId = NodeId,
    };
}
