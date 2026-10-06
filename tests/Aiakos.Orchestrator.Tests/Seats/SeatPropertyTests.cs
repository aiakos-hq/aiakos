using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

using CsCheck;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SeatPropertyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LaunchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly IHarnessStateProfile Profile = new ClaudeLike();
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes = new Dictionary<string, string>();
    private static readonly HarnessEventKind[] UnknownRecoveryEvents =
    [
        HarnessEventKind.ToolStarted,
        HarnessEventKind.InputResolved,
        HarnessEventKind.CompactionStarted,
    ];

    [Fact]
    public void T11AnyArrivalOrderOfCompleteClaudeTurnsConvergesToSourceOrder()
    {
        CompleteTurnGenerator.Sample(turn =>
        {
            var sourceOrder = AddTelemetry(turn.Events, turn.TelemetryMask);
            var arrivalOrder = Shuffle(sourceOrder, turn.Seed);
            var sourceResult = ApplyEvents(InitialState(), sourceOrder);
            var arrivalResult = ApplyEvents(InitialState(), arrivalOrder);

            AssertAxesEqual(sourceResult.State, arrivalResult.State);
            Assert.Equal(ActivityValue.Idle, arrivalResult.State.KnownActivity);
        }, iter: 1_000, threads: 1);
    }

    [Fact]
    public void T11RepeatingEventsAndRangesDoesNotChangeFinalAxesOrAcceptedCount()
    {
        CompleteTurnGenerator.Sample(turn =>
        {
            var events = AddTelemetry(turn.Events, turn.TelemetryMask);
            var firstPass = ApplyEvents(InitialState(), events);
            var replayed = ReplayInputs(firstPass.State, firstPass.Inputs);

            AssertAxesEqual(firstPass.State, replayed.State);
            Assert.True(firstPass.NonDuplicateCount > 0);
            Assert.Equal(0, replayed.NonDuplicateCount);
            Assert.All(replayed.Dispositions, disposition => Assert.Equal(EventDisposition.Duplicate, disposition));
        }, iter: 1_000, threads: 1);
    }

    [Fact]
    public void T11GapMakesActivityAndFreshOnlyResumabilityUnknownUntilRecoveryEvent()
    {
        (from missingSequences in Gen.Int[1, 5]
         from recoveryIndex in Gen.Int[0, UnknownRecoveryEvents.Length - 1]
         select (missingSequences, recoveryKind: UnknownRecoveryEvents[recoveryIndex]))
        .Sample(input =>
        {
            var state = InitialState();
            var gap = SeatStateMachine.Apply(state,
                new EventReceived(NodeId, input.missingSequences + 1, 0, LaunchId, new UnknownBody()),
                Profile, Now);

            Assert.Equal(ActivityValue.Unknown, gap.State.KnownActivity);
            Assert.Equal(SeatVocabulary.ActivityReasonObservationGap, gap.State.KnownActivityReason);
            Assert.Equal(ResumabilityValue.Unknown, gap.State.Resumability);
            Assert.Single(gap.Findings, finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
            Assert.Single(gap.Effects, effect => effect == new RequestCapture(LaunchId));
            SeatAssert.Invariants(gap);

            var recovery = SeatStateMachine.Apply(gap.State,
                new EventReceived(NodeId, gap.State.NextSeq, 1, LaunchId,
                    new HarnessBody(input.recoveryKind, "native-1", EmptyAttributes)), Profile, Now);
            Assert.NotEqual(ActivityValue.Unknown, recovery.State.KnownActivity);
            Assert.Equal(ResumabilityValue.Unknown, recovery.State.Resumability);
            SeatAssert.Invariants(recovery);
        }, iter: 1_000, threads: 1);
    }

    private static Gen<TurnCase> CompleteTurnGenerator =>
        Gen.Int[0, int.MaxValue].Select(BuildCompleteTurn);

    private static TurnCase BuildCompleteTurn(int seed)
    {
        var random = new Random(seed);
        var events = new List<EventSpec>();
        Add(HarnessEventKind.PromptSubmitted, EmptyAttributes);

        var toolCount = random.Next(0, 4);
        for (var tool = 0; tool < toolCount; tool++)
        {
            var toolId = $"tool-{tool}";
            Add(HarnessEventKind.ToolStarted, new Dictionary<string, string>
            {
                ["tool_name"] = "Search",
                ["tool_use_id"] = toolId,
            });
            if (random.Next(2) == 0)
            {
                Add(HarnessEventKind.InputRequested, new Dictionary<string, string>
                {
                    ["request_id"] = toolId,
                });
            }

            Add(HarnessEventKind.ToolFinished, new Dictionary<string, string>
            {
                ["tool_use_id"] = toolId,
            });
        }

        if (random.Next(2) == 0)
        {
            Add(HarnessEventKind.CompactionStarted, EmptyAttributes);
            Add(HarnessEventKind.Compacted, EmptyAttributes);
        }

        Add(HarnessEventKind.TurnEnded, EmptyAttributes);
        return new TurnCase(seed, events, random.Next(1 << Math.Min(events.Count + 1, 16)));

        void Add(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
            events.Add(new EventSpec(events.Count + 1, kind, "native-1", attributes));
    }

    private static List<EventSpec> AddTelemetry(IReadOnlyList<EventSpec> events, int mask)
    {
        var result = new List<EventSpec>(events.Count + events.Count / 2 + 1);
        for (var position = 0; position <= events.Count; position++)
        {
            if ((mask & (1 << (position % 16))) != 0)
                result.Add(new EventSpec(0, HarnessEventKind.Telemetry, "native-1", EmptyAttributes));
            if (position < events.Count)
                result.Add(events[position]);
        }

        return result;
    }

    private static EventSpec[] Shuffle(IReadOnlyList<EventSpec> events, int seed)
    {
        var result = events.ToArray();
        var random = new Random(seed ^ 0x5f3759df);
        for (var index = result.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (result[index], result[swap]) = (result[swap], result[index]);
        }

        return result;
    }

    private static ApplyResult ApplyEvents(SeatState initial, IReadOnlyList<EventSpec> events)
    {
        var state = initial;
        var nonDuplicateCount = 0;
        var dispositions = new List<EventDisposition?>();
        var inputs = new List<EventReceived>();
        for (var index = 0; index < events.Count; index++)
        {
            var entry = events[index];
            var input = new EventReceived(NodeId, state.NextSeq, entry.SourceSeq, LaunchId,
                new HarnessBody(entry.Kind, entry.NativeSessionId, entry.Attributes));
            inputs.Add(input);
            var step = SeatStateMachine.Apply(state, input, Profile, Now);
            SeatAssert.Invariants(step);
            if (step.Disposition != EventDisposition.Duplicate)
                nonDuplicateCount++;
            dispositions.Add(step.Disposition);
            state = step.State;
        }

        return new ApplyResult(state, nonDuplicateCount, dispositions, inputs);
    }

    private static ApplyResult ReplayInputs(SeatState initial, IReadOnlyList<EventReceived> inputs)
    {
        var state = initial;
        var nonDuplicateCount = 0;
        var dispositions = new List<EventDisposition?>();
        foreach (var input in inputs)
        {
            var step = SeatStateMachine.Apply(state, input, Profile, Now);
            SeatAssert.Invariants(step);
            if (step.Disposition != EventDisposition.Duplicate)
                nonDuplicateCount++;
            dispositions.Add(step.Disposition);
            state = step.State;
        }

        return new ApplyResult(state, nonDuplicateCount, dispositions, inputs);
    }

    private static void AssertAxesEqual(SeatState expected, SeatState actual)
    {
        Assert.Equal(expected.KnownSession, actual.KnownSession);
        Assert.Equal(expected.KnownActivity, actual.KnownActivity);
        Assert.Equal(expected.Resumability, actual.Resumability);
        Assert.Equal(expected.KnownSessionReason, actual.KnownSessionReason);
        Assert.Equal(expected.KnownActivityReason, actual.KnownActivityReason);
        Assert.Equal(expected.ResumabilityReason, actual.ResumabilityReason);
    }

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

    private sealed record TurnCase(int Seed, IReadOnlyList<EventSpec> Events, int TelemetryMask);
    private sealed record EventSpec(long SourceSeq, HarnessEventKind Kind, string NativeSessionId,
        IReadOnlyDictionary<string, string> Attributes);
    private sealed record ApplyResult(SeatState State, int NonDuplicateCount,
        IReadOnlyList<EventDisposition?> Dispositions, IReadOnlyList<EventReceived> Inputs);
}
