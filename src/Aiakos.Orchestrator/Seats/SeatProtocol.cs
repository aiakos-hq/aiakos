using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

public sealed record SeatKey(Guid TenantId, Guid SeatId);
public sealed record SeatEnvelope(Guid TenantId, Guid SeatId, object Message);
public sealed record SeatEventsCommitted(Guid SeatId, Guid NodeInstanceId, long ThroughSeq);
public sealed record SeatInputCommitted(Guid SeatId, long Version, SeatState State,
    IReadOnlyList<SeatEffect> Effects);
public sealed record SeatActorReloaded(Guid TenantId, Guid SeatId, long Version);

public sealed class SeatCommitFailedException : InvalidOperationException
{
    public SeatCommitFailedException() : base("SEAT_COMMIT_FAILED") { }

    public SeatCommitFailedException(string? message) : base("SEAT_COMMIT_FAILED") { }

    public SeatCommitFailedException(string? message, Exception? innerException)
        : base("SEAT_COMMIT_FAILED") { }
}

public interface ISeatEventCommitter
{
    Task<SeatEventsCommitted> CommitAsync(Guid tenantId, Guid seatId, string nodeName,
        Guid nodeInstanceId, IReadOnlyList<SeatEvent> events, string? traceParent, CancellationToken ct);
}

public interface ISeatInputCommitter
{
    Task<SeatInputCommitted> ApplyAsync(Guid tenantId, Guid seatId, SeatInput input, CancellationToken ct);
}

public interface ISeatActorLifecycle
{
    IDisposable Subscribe(Action<SeatActorReloaded> observer);
}

public sealed record SeatStoredCommand(Guid CommandId, Guid? LaunchId, SeatCommandKind Kind,
    string Status, string? Outcome);

public sealed record SeatActorSnapshot(SeatKey Key, string Kind, string? Harness, string? Node,
    bool Retired, long Version, Guid? CurrentSessionId, SeatState State,
    IReadOnlyDictionary<Guid, SeatStoredCommand> Commands);

public sealed record SeatAppliedInput(SeatInput Input, SeatStep Step, DateTimeOffset At,
    SeatEvent? Event, string? TraceParent);

public sealed record SeatStoreReceipt(long Version, Guid? CurrentSessionId);
public sealed record SeatNativeSession(Guid SeatId, Guid SessionId);

internal sealed class SeatStoreRejectedException : InvalidOperationException
{
    internal SeatStoreRejectedException() : base("SEAT_COMMIT_REJECTED") { }
}

public interface ISeatActorReader
{
    Task<SeatActorSnapshot?> LoadAsync(SeatKey key, CancellationToken ct);
    Task<IReadOnlyList<SeatKey>> GetStartupSeatsAsync(CancellationToken ct);
    Task<SeatNativeSession?> FindNativeSessionAsync(Guid tenantId, string harness,
        string nativeSessionId, CancellationToken ct);
    Task<IReadOnlySet<long>> GetCommittedSequencesAsync(SeatKey key, Guid nodeInstanceId,
        IReadOnlyList<long> sequences, CancellationToken ct);
}

public interface ISeatActorWriter
{
    Task<SeatStoreReceipt> CommitAsync(SeatActorSnapshot before,
        IReadOnlyList<SeatAppliedInput> inputs, CancellationToken ct);
    Task RecordActorStoppedAsync(SeatKey key, DateTimeOffset at, CancellationToken ct);
}

internal sealed record SeatEventRequest(string NodeName, Guid NodeInstanceId,
    IReadOnlyList<SeatEvent> Events, string? TraceParent,
    TaskCompletionSource<SeatEventsCommitted> Completion);

internal sealed record SeatInputRequest(SeatInput Input,
    TaskCompletionSource<SeatInputCommitted> Completion);

internal sealed record SeatChildLoaded(SeatKey Key, long Version, TaskCompletionSource Ready);
