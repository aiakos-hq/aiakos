using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

public interface IHarnessStateProfile
{
    string Harness { get; }
    string NewNativeSessionId();
    bool IsValidNativeSessionId(string id);
    bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes);
    bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes);
    bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes);
    bool FreshRelaunchReusesSessionId { get; }
    bool EmitsInputResolved { get; }
    TimeSpan ReadyTimeout { get; }
    TimeSpan ConfirmTimeout { get; }
    TimeSpan QuietTimeout { get; }
}

public enum SessionValue { Absent, Starting, Present, Exited, Unknown }
public enum ActivityValue { None, Idle, Working, NeedsInput, Unknown }
public enum ResumabilityValue { None, FreshOnly, Resumable, Lost, Unknown }
public enum SeatOverlay { NodeLinkLost, OrchestratorRestarted }
public enum SeatDesired { Down, Up }
public enum EventDisposition { Applied, Late, StaleLaunch, Orphan, Evidence, Duplicate }

public sealed record CurrentLaunch(Guid LaunchId, LaunchMode Mode, bool ReusedNativeSessionId, bool StopRequested);

public sealed record SeatState
{
    public required SessionValue Session { get; init; }
    public required string? SessionReason { get; init; }
    public required DateTimeOffset? SessionSince { get; init; }
    public required ActivityValue Activity { get; init; }
    public required string? ActivityDetail { get; init; }
    public required string? ActivityReason { get; init; }
    public required DateTimeOffset? ActivitySince { get; init; }
    public required ResumabilityValue Resumability { get; init; }
    public required string? ResumabilityReason { get; init; }
    public required DateTimeOffset? ResumabilitySince { get; init; }
    public required SeatOverlay? Overlay { get; init; }
    public required SessionValue KnownSession { get; init; }
    public required string? KnownSessionReason { get; init; }
    public required ActivityValue KnownActivity { get; init; }
    public required string? KnownActivityDetail { get; init; }
    public required string? KnownActivityReason { get; init; }
    public required SeatDesired Desired { get; init; }
    public required CurrentLaunch? Launch { get; init; }
    public required string? NativeSessionId { get; init; }
    public required string? PendingInputRequest { get; init; }
    public required ActivityValue? PreCompactionActivity { get; init; }
    public required bool ReadinessSeen { get; init; }
    public required Guid? NodeInstanceId { get; init; }
    public required long NextSeq { get; init; }
    public required long LastSourceSeq { get; init; }
    public required long? CatchUpSeq { get; init; }
    public required DateTimeOffset? LastEventAt { get; init; }

    public static SeatState Initial(DateTimeOffset now) => new()
    {
        Session = SessionValue.Absent,
        SessionReason = null,
        SessionSince = now,
        Activity = ActivityValue.None,
        ActivityDetail = null,
        ActivityReason = null,
        ActivitySince = now,
        Resumability = ResumabilityValue.None,
        ResumabilityReason = null,
        ResumabilitySince = now,
        Overlay = null,
        KnownSession = SessionValue.Absent,
        KnownSessionReason = null,
        KnownActivity = ActivityValue.None,
        KnownActivityDetail = null,
        KnownActivityReason = null,
        Desired = SeatDesired.Down,
        Launch = null,
        NativeSessionId = null,
        PendingInputRequest = null,
        PreCompactionActivity = null,
        ReadinessSeen = false,
        NodeInstanceId = null,
        NextSeq = 1,
        LastSourceSeq = 0,
        CatchUpSeq = null,
        LastEventAt = null
    };
}

public abstract record SeatInput;
public sealed record UpRequested(bool Fresh, bool NodeConnected, Guid NewLaunchId, string NewNativeSessionId) : SeatInput;
public sealed record DownRequested : SeatInput;
public sealed record SendRequested(bool Force, bool NodeConnected, bool DeliveryInFlight) : SeatInput;
public sealed record NodeAttached(Guid NodeInstanceId, SeatInventoryEntry? Inventory) : SeatInput;
public sealed record SeatInventoryEntry(Guid? LaunchId, SessionLifecycle Lifecycle, long LastSeq);
public sealed record NodeLinkLost : SeatInput;
public sealed record OrchestratorRestarted : SeatInput;
public sealed record CommandDispatchFailed(SeatCommandKind Kind) : SeatInput;
public enum SeatCommandKind { Start, Deliver, Keys, Capture, Stop }
public sealed record QuietTimeoutFired : SeatInput;
public sealed record LaunchWatchdogFired : SeatInput;
public sealed record UnknownProlongedFired : SeatInput;
public sealed record EventReceived(Guid NodeInstanceId, long Seq, long SourceSeq, Guid? LaunchId, SeatEventBody Body) : SeatInput;

public abstract record SeatEventBody;
public sealed record HarnessBody(HarnessEventKind Kind, string NativeSessionId,
    IReadOnlyDictionary<string, string> Attributes) : SeatEventBody;
public sealed record LaunchResultBody(LaunchOutcome Outcome, string Reason, int? ExitCode) : SeatEventBody;
public sealed record StartNotCompletedBody(CommandStatus Status, string Reason) : SeatEventBody;
public sealed record StopResultBody(StopOutcome Outcome) : SeatEventBody;
public sealed record StopNotCompletedBody(CommandStatus Status) : SeatEventBody;
public sealed record CaptureBody(bool PaneDead) : SeatEventBody;
public sealed record OtherCommandResultBody : SeatEventBody;
public sealed record SessionObservedBody(string NativeSessionId, bool MatchesExpected) : SeatEventBody;
public sealed record ProcessExitedBody(int? ExitCode, int? Signal) : SeatEventBody;
public sealed record ObservationGapBody(GapReason Reason) : SeatEventBody;
public sealed record UnknownBody : SeatEventBody;

public sealed record SeatStep(SeatState State, SeatReply? Reply, EventDisposition? Disposition,
    IReadOnlyList<SeatTransition> Transitions, IReadOnlyList<FindingChange> Findings, IReadOnlyList<SeatEffect> Effects);
public sealed record SeatTransition(string Axis, bool Reported, string From, string To, string? Reason, string Rule);
public sealed record FindingChange(string Kind, bool Open);
public abstract record SeatReply;
public sealed record Accepted(bool Forced = false) : SeatReply;
public sealed record AlreadyUp : SeatReply;
public sealed record Rejected(string Reason) : SeatReply;
public abstract record SeatEffect;
public sealed record StartLaunch(Guid LaunchId, LaunchMode Mode, string Decision, string NativeSessionId,
    bool NewSession, bool AbandonPreviousSession) : SeatEffect;
public sealed record DispatchStop(Guid LaunchId) : SeatEffect;
public sealed record RequestCapture(Guid LaunchId) : SeatEffect;
public sealed record AdoptRotatedSession(string NativeSessionId, string PreviousNativeSessionId) : SeatEffect;

public enum DeliveryState { Pending, Sent, Confirmed, SubmittedUnconfirmed, NotDelivered, Failed, Unknown }
public abstract record DeliveryInput;
public sealed record DeliveryDispatched : DeliveryInput;
public sealed record DeliveryDispatchFailed : DeliveryInput;
public sealed record DeliveryResultArrived(DeliveryOutcome Outcome) : DeliveryInput;
public sealed record DeliveryNotCompleted(CommandStatus Status) : DeliveryInput;
public sealed record DeliveryNodeReplaced : DeliveryInput;
public sealed record DeliveryStep(DeliveryState State, IReadOnlyList<FindingChange> Findings);
