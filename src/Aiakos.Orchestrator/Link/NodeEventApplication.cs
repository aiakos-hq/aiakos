using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Link;

public interface INodeEventCommitter
{
    Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct);

    Task<EventAck?> CommitAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request,
        CancellationToken ct);

    Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct);
}

public sealed record NodeEventCursorResult(bool Duplicate, bool Gap, Guid NodeInstanceId, ulong NextSeq);

public static class NodeEventCursor
{
    public static NodeEventCursorResult Evaluate(Guid? committedEpoch, ulong nextSeq, Guid incomingEpoch, ulong seq)
    {
        if (committedEpoch == Guid.Empty || incomingEpoch == Guid.Empty || nextSeq is 0 or > long.MaxValue ||
            seq is 0 or >= long.MaxValue)
        {
            throw new ArgumentException("Invalid event cursor.");
        }

        if (committedEpoch is null)
            return new NodeEventCursorResult(false, seq > 1, incomingEpoch, seq + 1);

        if (committedEpoch.Value != incomingEpoch)
            return new NodeEventCursorResult(false, true, incomingEpoch, seq + 1);

        if (seq < nextSeq)
            return new NodeEventCursorResult(true, false, committedEpoch.Value, nextSeq);

        return new NodeEventCursorResult(false, seq > nextSeq, committedEpoch.Value, seq + 1);
    }
}

public interface INodeEventApplication
{
    Task<EventAck?> ReceiveEventAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request,
        CancellationToken ct);
}

public sealed class EventNodeLinkApplication(INodeEventCommitter committer) : INodeLinkApplication, INodeEventApplication
{
    private const string InvalidEventMessage = "Invalid seat event.";
    private const string InvalidAckMessage = "Invalid committed event acknowledgement.";
    private const string InvalidReplayMessage = "Invalid committed event replay.";

    public async Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await committer.GetReplayAsync(identity, hello.Clone(), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (result is null)
            throw InvalidReplay();

        var seatIds = new HashSet<Guid>();
        var snapshots = new ReplayFrom[result.Count];
        for (var index = 0; index < result.Count; index++)
        {
            var replay = result[index];
            if (replay is null || !Guid.TryParse(replay.SeatId, out var seatId) || seatId == Guid.Empty ||
                replay.NextSeq is 0 or > long.MaxValue || !seatIds.Add(seatId))
            {
                throw InvalidReplay();
            }

            snapshots[index] = replay.Clone();
        }

        return snapshots;
    }

    public Task ReceiveAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request?.BodyCase == ConnectRequest.BodyOneofCase.SeatEvent)
            throw new InvalidOperationException("Seat events require committed acknowledgement.");

        return Task.CompletedTask;
    }

    public async Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await committer.StateChangedAsync(identity, state, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    public async Task<EventAck?> ReceiveEventAsync(NodeIdentity identity, string nodeInstanceId,
        ConnectRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValidEvent(nodeInstanceId, request))
            throw new ArgumentException(InvalidEventMessage);

        var seatEvent = request.SeatEvent;
        var seatId = Guid.Parse(seatEvent.SeatId);
        var seq = seatEvent.Seq;
        var committedAck = await committer.CommitAsync(identity, nodeInstanceId, request.Clone(), ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (committedAck is null)
            return null;

        if (!Guid.TryParse(committedAck.SeatId, out var acknowledgedSeat) || acknowledgedSeat != seatId ||
            committedAck.ThroughSeq < seq || committedAck.ThroughSeq >= long.MaxValue)
        {
            throw new InvalidOperationException(InvalidAckMessage);
        }

        return committedAck.Clone();
    }

    private static bool IsValidEvent(string nodeInstanceId, ConnectRequest? request)
    {
        if (!Guid.TryParse(nodeInstanceId, out var epoch) || epoch == Guid.Empty ||
            request?.BodyCase != ConnectRequest.BodyOneofCase.SeatEvent)
        {
            return false;
        }

        var seatEvent = request.SeatEvent;
        if (!Guid.TryParse(seatEvent.SeatId, out var seatId) || seatId == Guid.Empty ||
            (seatEvent.LaunchId.Length > 0 && !Guid.TryParse(seatEvent.LaunchId, out _)) ||
            seatEvent.Seq is 0 or >= long.MaxValue || seatEvent.SourceSeq > long.MaxValue ||
            seatEvent.ObservedAt is null || !IsValidTimestamp(seatEvent.ObservedAt))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidTimestamp(Google.Protobuf.WellKnownTypes.Timestamp timestamp) =>
        timestamp.Seconds is >= -62135596800 and <= 253402300799 &&
        timestamp.Nanos is >= 0 and < 1_000_000_000;

    private static InvalidOperationException InvalidReplay() => new(InvalidReplayMessage);
}
