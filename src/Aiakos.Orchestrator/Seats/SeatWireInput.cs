using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

internal static class SeatWireInput
{
    internal static EventReceived Map(Guid nodeInstanceId, SeatEvent value,
        IReadOnlyDictionary<Guid, SeatStoredCommand> commands)
    {
        var seq = checked((long)value.Seq);
        var sourceSeq = checked((long)value.SourceSeq);
        Guid? launchId = string.IsNullOrEmpty(value.LaunchId) ? null : Guid.Parse(value.LaunchId);

        SeatEventBody body;
        if (ContainsNull(value))
        {
            body = new ObservationGapBody(GapReason.IngestUnavailable);
        }
        else
        {
            body = value.BodyCase switch
            {
                SeatEvent.BodyOneofCase.Harness => MapHarness(value.Harness),
                SeatEvent.BodyOneofCase.SessionObserved => new SessionObservedBody(
                    value.SessionObserved.NativeSessionId, value.SessionObserved.MatchesExpected),
                SeatEvent.BodyOneofCase.ProcessExited => new ProcessExitedBody(
                    value.ProcessExited.HasExitCode ? value.ProcessExited.ExitCode : null,
                    value.ProcessExited.HasSignal ? value.ProcessExited.Signal : null),
                SeatEvent.BodyOneofCase.Gap => new ObservationGapBody(value.Gap.Reason),
                SeatEvent.BodyOneofCase.CommandResult => MapCommandResult(value.CommandResult, commands),
                _ => new UnknownBody()
            };
        }

        return new EventReceived(nodeInstanceId, seq, sourceSeq, launchId, body);
    }

    private static HarnessBody MapHarness(HarnessEvent value) => new(value.Kind, value.NativeSessionId,
        new Dictionary<string, string>(value.Attributes, StringComparer.Ordinal));

    private static SeatEventBody MapCommandResult(CommandResult value,
        IReadOnlyDictionary<Guid, SeatStoredCommand> commands)
    {
        if (value.Status == CommandStatus.Completed)
        {
            return value.ResultCase switch
            {
                CommandResult.ResultOneofCase.Launch => new LaunchResultBody(value.Launch.Outcome,
                    value.Launch.Reason, value.Launch.HasExitCode ? value.Launch.ExitCode : null),
                CommandResult.ResultOneofCase.Stop => new StopResultBody(value.Stop.Outcome),
                CommandResult.ResultOneofCase.Capture => new CaptureBody(value.Capture.PaneDead),
                _ => new OtherCommandResultBody()
            };
        }

        if (!Guid.TryParse(value.CommandId, out var commandId) || !commands.TryGetValue(commandId, out var command))
            return new OtherCommandResultBody();

        return command.Kind switch
        {
            SeatCommandKind.Start => new StartNotCompletedBody(value.Status, value.Error?.Reason ?? string.Empty),
            SeatCommandKind.Stop => new StopNotCompletedBody(value.Status),
            _ => new OtherCommandResultBody()
        };
    }

    private static bool ContainsNull(SeatEvent value) => value.BodyCase switch
    {
        SeatEvent.BodyOneofCase.Harness => ContainsNull(value.Harness),
        SeatEvent.BodyOneofCase.SessionObserved => HasNull(value.SessionObserved.NativeSessionId),
        SeatEvent.BodyOneofCase.CommandResult => ContainsNull(value.CommandResult),
        _ => false
    };

    private static bool ContainsNull(HarnessEvent value) =>
        HasNull(value.Harness) || HasNull(value.NativeName) || HasNull(value.NativeSessionId) ||
        HasNull(value.RawContentType) || (value.Usage is not null && HasNull(value.Usage.ModelId)) ||
        value.Attributes.Any(pair => HasNull(pair.Key) || HasNull(pair.Value));

    private static bool ContainsNull(CommandResult value)
    {
        if (HasNull(value.CommandId) || (value.Error is not null && ContainsNull(value.Error)))
            return true;

        return value.ResultCase switch
        {
            CommandResult.ResultOneofCase.Launch => HasNull(value.Launch.Reason) ||
                HasNull(value.Launch.ObservedSessionId) ||
                (value.Launch.Evidence is not null && HasNull(value.Launch.Evidence.Text)),
            CommandResult.ResultOneofCase.Delivery => HasNull(value.Delivery.TurnId),
            CommandResult.ResultOneofCase.Capture => HasNull(value.Capture.Text),
            _ => false
        };
    }

    private static bool ContainsNull(Error value) => HasNull(value.Reason) || HasNull(value.Message) ||
        value.Metadata.Any(pair => HasNull(pair.Key) || HasNull(pair.Value));

    private static bool HasNull(string value) => value.Contains('\0');
}
