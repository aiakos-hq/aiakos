using Aiakos.Contracts.Node.V1;

namespace Aiakos.Node.Hooks;

public sealed record HookLaunch(string SeatId, string SeatInstanceId, string LaunchId, string SeatAddress);

public sealed record IngestedPayload(HookLaunch Launch, string Kind, string Name,
    ulong SourceSeq, DateTimeOffset ReceivedAt, byte[] Body)
{
    public override string ToString() => $"IngestedPayload {{ Launch = {Launch}, Kind = {Kind}, Name = {Name}, SourceSeq = {SourceSeq}, ReceivedAt = {ReceivedAt} }}";
}

public interface IHookIngestConsumer
{
    ValueTask ReceiveAsync(IngestedPayload payload, CancellationToken ct);
    ValueTask GapAsync(HookLaunch launch, ObservationGap gap, CancellationToken ct);
}
