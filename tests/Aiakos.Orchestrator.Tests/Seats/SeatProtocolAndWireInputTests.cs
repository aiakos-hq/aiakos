using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SeatProtocolAndWireInputTests
{
    private static readonly Guid NodeInstanceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid LaunchId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CommandId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void ProtocolRecordsExposeImmutableExactDataAndFailuresAreSecretSafe()
    {
        var seat = new SeatKey(Guid.NewGuid(), Guid.NewGuid());
        var envelope = new SeatEnvelope(seat.TenantId, seat.SeatId, "input");
        var events = new SeatEventsCommitted(seat.SeatId, NodeInstanceId, 12);
        var input = new SeatInputCommitted(seat.SeatId, 8, SeatState.Initial(DateTimeOffset.UnixEpoch), []);
        var reloaded = new SeatActorReloaded(seat.TenantId, seat.SeatId, 8);

        Assert.Equal(seat.TenantId, envelope.TenantId);
        Assert.Equal(seat.SeatId, events.SeatId);
        Assert.Equal(NodeInstanceId, events.NodeInstanceId);
        Assert.Equal(12, events.ThroughSeq);
        Assert.Equal(8, input.Version);
        Assert.Equal(seat.TenantId, reloaded.TenantId);
        Assert.Equal(seat.SeatId, reloaded.SeatId);
        Assert.Equal(seat, new SeatKey(seat.TenantId, seat.SeatId));
        Assert.Equal("Pending", new SeatStoredCommand(CommandId, null, SeatCommandKind.Start, "Pending", null).Status);
        Assert.Equal(seat, new SeatActorSnapshot(seat, "agent", null, null, false, 8, null,
            SeatState.Initial(DateTimeOffset.UnixEpoch), new Dictionary<Guid, SeatStoredCommand>()).Key);
        Assert.Equal(8, new SeatStoreReceipt(8, null).Version);
        Assert.Equal(seat.SeatId, new SeatNativeSession(seat.SeatId, LaunchId).SeatId);
        Assert.Equal(1, new SeatAppliedInput(new NodeLinkLost(),
            new SeatStep(SeatState.Initial(DateTimeOffset.UnixEpoch), null, null, [], [], []),
            DateTimeOffset.UnixEpoch, null, null).Step.State.NextSeq);

        AssertFailureIsSanitized(new SeatCommitFailedException());
        AssertFailureIsSanitized(new SeatCommitFailedException("sensitive"));
        AssertFailureIsSanitized(new SeatCommitFailedException("sensitive", new InvalidOperationException("secret")));

        var protocols = new[] { typeof(ISeatEventCommitter), typeof(ISeatInputCommitter), typeof(ISeatActorLifecycle) };
        Assert.Equal(3, protocols.Distinct().Count());
        AssertProperties(typeof(SeatKey), "TenantId", "SeatId");
        AssertProperties(typeof(SeatEnvelope), "TenantId", "SeatId", "Message");
        AssertProperties(typeof(SeatEventsCommitted), "SeatId", "NodeInstanceId", "ThroughSeq");
        AssertProperties(typeof(SeatInputCommitted), "SeatId", "Version", "State", "Effects");
        AssertProperties(typeof(SeatActorReloaded), "TenantId", "SeatId", "Version");
        AssertProperties(typeof(SeatStoredCommand), "CommandId", "LaunchId", "Kind", "Status", "Outcome");
        AssertProperties(typeof(SeatActorSnapshot), "Key", "Kind", "Harness", "Node", "Retired", "Version",
            "CurrentSessionId", "State", "Commands");
        AssertProperties(typeof(SeatAppliedInput), "Input", "Step", "At", "Event", "TraceParent");
        AssertProperties(typeof(SeatStoreReceipt), "Version", "CurrentSessionId");
        AssertProperties(typeof(SeatNativeSession), "SeatId", "SessionId");
        AssertMethod(typeof(ISeatEventCommitter), "CommitAsync", typeof(Task<SeatEventsCommitted>), typeof(Guid),
            typeof(Guid), typeof(string), typeof(Guid), typeof(IReadOnlyList<SeatEvent>), typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(ISeatInputCommitter), "ApplyAsync", typeof(Task<SeatInputCommitted>), typeof(Guid),
            typeof(Guid), typeof(SeatInput), typeof(CancellationToken));
        AssertMethod(typeof(ISeatActorLifecycle), "Subscribe", typeof(IDisposable), typeof(Action<SeatActorReloaded>));
        AssertMethod(typeof(ISeatActorReader), "LoadAsync", typeof(Task<SeatActorSnapshot>), typeof(SeatKey), typeof(CancellationToken));
        AssertMethod(typeof(ISeatActorReader), "GetStartupSeatsAsync", typeof(Task<IReadOnlyList<SeatKey>>), typeof(CancellationToken));
        AssertMethod(typeof(ISeatActorReader), "FindNativeSessionAsync", typeof(Task<SeatNativeSession>), typeof(Guid),
            typeof(string), typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(ISeatActorReader), "GetCommittedSequencesAsync", typeof(Task<IReadOnlySet<long>>), typeof(SeatKey),
            typeof(Guid), typeof(IReadOnlyList<long>), typeof(CancellationToken));
        AssertMethod(typeof(ISeatActorWriter), "CommitAsync", typeof(Task<SeatStoreReceipt>), typeof(SeatActorSnapshot),
            typeof(IReadOnlyList<SeatAppliedInput>), typeof(CancellationToken));
        AssertMethod(typeof(ISeatActorWriter), "RecordActorStoppedAsync", typeof(Task), typeof(SeatKey),
            typeof(DateTimeOffset), typeof(CancellationToken));

        var rejected = new SeatStoreRejectedException();
        Assert.Equal("SEAT_COMMIT_REJECTED", rejected.Message);
        Assert.Null(rejected.InnerException);
    }

    [Fact]
    public void MapsEveryNonCommandBodyAndPreservesOptionalAndUnknownValues()
    {
        var harnessEvent = new HarnessEvent
        {
            Kind = (HarnessEventKind)123,
            NativeSessionId = "native",
            Attributes = { ["source"] = "startup" }
        };
        var harness = SeatWireInput.Map(NodeInstanceId, Event(harness: harnessEvent), new Dictionary<Guid, SeatStoredCommand>());
        var harnessBody = Assert.IsType<HarnessBody>(harness.Body);
        Assert.Equal((HarnessEventKind)123, harnessBody.Kind);
        Assert.Equal("native", harnessBody.NativeSessionId);
        Assert.Equal("startup", harnessBody.Attributes["source"]);
        Assert.NotSame(harnessEvent.Attributes, harnessBody.Attributes);
        Assert.Same(StringComparer.Ordinal, ((Dictionary<string, string>)harnessBody.Attributes).Comparer);

        Assert.Equal(new SessionObservedBody("observed", true), SeatWireInput.Map(NodeInstanceId,
            Event(sessionObserved: new SessionObserved { NativeSessionId = "observed", MatchesExpected = true }), EmptyCommands()).Body);
        Assert.Equal(new ProcessExitedBody(null, null), SeatWireInput.Map(NodeInstanceId,
            Event(processExited: new ProcessExited()), EmptyCommands()).Body);
        Assert.Equal(new ProcessExitedBody(0, 0), SeatWireInput.Map(NodeInstanceId,
            Event(processExited: new ProcessExited { ExitCode = 0, Signal = 0 }), EmptyCommands()).Body);
        Assert.Equal(new ObservationGapBody((GapReason)77), SeatWireInput.Map(NodeInstanceId,
            Event(gap: new ObservationGap { Reason = (GapReason)77 }), EmptyCommands()).Body);
        Assert.IsType<UnknownBody>(SeatWireInput.Map(NodeInstanceId, Event(), EmptyCommands()).Body);
    }

    [Fact]
    public void MapsCompletedTypedCommandResultsRegardlessOfCommandDictionary()
    {
        var launch = new CommandResult
        {
            Status = CommandStatus.Completed,
            Launch = new LaunchResult { Outcome = (LaunchOutcome)88, Reason = "reason", ExitCode = 0 }
        };
        Assert.Equal(new LaunchResultBody((LaunchOutcome)88, "reason", 0),
            SeatWireInput.Map(NodeInstanceId, Event(commandResult: launch), EmptyCommands()).Body);
        launch.Launch.ExitCode = 0;
        launch.Launch.ClearExitCode();
        Assert.Equal(new LaunchResultBody((LaunchOutcome)88, "reason", null),
            SeatWireInput.Map(NodeInstanceId, Event(commandResult: launch), EmptyCommands()).Body);

        var stop = new CommandResult
        {
            Status = CommandStatus.Completed,
            Stop = new StopResult { Outcome = (StopOutcome)89 }
        };
        Assert.Equal(new StopResultBody((StopOutcome)89),
            SeatWireInput.Map(NodeInstanceId, Event(commandResult: stop), EmptyCommands()).Body);

        var capture = new CommandResult
        {
            Status = CommandStatus.Completed,
            Capture = new PaneCapture { PaneDead = true }
        };
        Assert.Equal(new CaptureBody(true),
            SeatWireInput.Map(NodeInstanceId, Event(commandResult: capture), EmptyCommands()).Body);

        var delivery = new CommandResult { Status = CommandStatus.Completed, Delivery = new DeliveryResult() };
        Assert.IsType<OtherCommandResultBody>(SeatWireInput.Map(NodeInstanceId, Event(commandResult: delivery), EmptyCommands()).Body);
    }

    [Fact]
    public void MapsEveryNoncompletedStatusByStoredCommandKind()
    {
        var commands = new Dictionary<Guid, SeatStoredCommand>
        {
            [CommandId] = new(CommandId, null, SeatCommandKind.Start, "Pending", null),
            [LaunchId] = new(LaunchId, null, SeatCommandKind.Stop, "Pending", null),
            [Guid.Parse("44444444-4444-4444-8444-444444444444")] = new(Guid.Parse("44444444-4444-4444-8444-444444444444"), null, SeatCommandKind.Deliver, "Pending", null)
        };
        foreach (var status in new[] { CommandStatus.Unspecified, CommandStatus.Rejected, CommandStatus.Failed, CommandStatus.TimedOut, (CommandStatus)99 })
        {
            var failedStart = new CommandResult
            {
                CommandId = CommandId.ToString(),
                Status = status,
                Error = new Error { Reason = "START_FAILED" }
            };
            Assert.Equal(new StartNotCompletedBody(status, "START_FAILED"),
                SeatWireInput.Map(NodeInstanceId, Event(commandResult: failedStart), commands).Body);

            var failedStop = new CommandResult { CommandId = LaunchId.ToString(), Status = status };
            Assert.Equal(new StopNotCompletedBody(status),
                SeatWireInput.Map(NodeInstanceId, Event(commandResult: failedStop), commands).Body);

            var failedDelivery = new CommandResult
            {
                CommandId = "44444444-4444-4444-8444-444444444444", Status = status
            };
            Assert.IsType<OtherCommandResultBody>(SeatWireInput.Map(NodeInstanceId,
                Event(commandResult: failedDelivery), commands).Body);
        }

        var missingId = new CommandResult { Status = CommandStatus.Failed };
        Assert.IsType<OtherCommandResultBody>(SeatWireInput.Map(NodeInstanceId, Event(commandResult: missingId), commands).Body);
        var invalidId = new CommandResult { CommandId = "invalid", Status = CommandStatus.Failed };
        Assert.IsType<OtherCommandResultBody>(SeatWireInput.Map(NodeInstanceId, Event(commandResult: invalidId), commands).Body);
    }

    [Fact]
    public void PreservesTrustedEpochSequencesLaunchAndNulEvidenceFallback()
    {
        var value = Event(sourceSeq: 45);
        value.Seq = 44;
        var mapped = SeatWireInput.Map(NodeInstanceId, value, EmptyCommands());
        Assert.Equal(NodeInstanceId, mapped.NodeInstanceId);
        Assert.Equal(44, mapped.Seq);
        Assert.Equal(45, mapped.SourceSeq);
        Assert.Equal(LaunchId, mapped.LaunchId);

        value.LaunchId = string.Empty;
        Assert.Null(SeatWireInput.Map(NodeInstanceId, value, EmptyCommands()).LaunchId);

        var nulEvidence = Event(harness: new HarnessEvent
        {
            NativeSessionId = "native\0id",
            Attributes = { ["attribute"] = "value" }
        });
        Assert.Equal(new ObservationGapBody(GapReason.IngestUnavailable),
            SeatWireInput.Map(NodeInstanceId, nulEvidence, EmptyCommands()).Body);

        var malformed = Event();
        malformed.Seq = ulong.MaxValue;
        Assert.Throws<OverflowException>(() => SeatWireInput.Map(NodeInstanceId, malformed, EmptyCommands()));
        malformed.Seq = 1;
        malformed.SourceSeq = ulong.MaxValue;
        Assert.Throws<OverflowException>(() => SeatWireInput.Map(NodeInstanceId, malformed, EmptyCommands()));
        malformed.SourceSeq = 1;
        malformed.LaunchId = "not-a-guid";
        Assert.Throws<FormatException>(() => SeatWireInput.Map(NodeInstanceId, malformed, EmptyCommands()));
    }

    private static Dictionary<Guid, SeatStoredCommand> EmptyCommands() => [];

    private static SeatEvent Event(CommandResult? commandResult = null, HarnessEvent? harness = null,
        SessionObserved? sessionObserved = null, ProcessExited? processExited = null, ObservationGap? gap = null,
        ulong sourceSeq = 2)
    {
        var value = new SeatEvent { Seq = 1, SourceSeq = sourceSeq, LaunchId = LaunchId.ToString() };
        if (commandResult is not null) value.CommandResult = commandResult;
        if (harness is not null) value.Harness = harness;
        if (sessionObserved is not null) value.SessionObserved = sessionObserved;
        if (processExited is not null) value.ProcessExited = processExited;
        if (gap is not null) value.Gap = gap;
        return value;
    }

    private static void AssertFailureIsSanitized(SeatCommitFailedException exception)
    {
        Assert.Equal("SEAT_COMMIT_FAILED", exception.Message);
        Assert.Null(exception.InnerException);
    }

    private static void AssertProperties(Type recordType, params string[] expectedNames)
    {
        var properties = recordType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(expectedNames.Order(), properties.Select(property => property.Name).Order());
        Assert.All(properties, property =>
            Assert.Contains(typeof(IsExternalInit), property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
    }

    private static void AssertMethod(Type interfaceType, string methodName, Type returnType, params Type[] parameterTypes)
    {
        var method = interfaceType.GetMethod(methodName)!;
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(parameterTypes, method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static async Task CompilePortSignatures(ISeatEventCommitter events, ISeatInputCommitter inputs,
        ISeatActorLifecycle lifecycle, Guid tenantId, Guid seatId, SeatInput input, CancellationToken ct)
    {
        await events.CommitAsync(tenantId, seatId, "node", NodeInstanceId, [], null, ct);
        await inputs.ApplyAsync(tenantId, seatId, input, ct);
        using var subscription = lifecycle.Subscribe(_ => { });
    }
}
