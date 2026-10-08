namespace Aiakos.Node.Sessions;

public interface ISessionHost
{
    SessionHostInfo Info { get; }
    Task<SessionHandle> StartAsync(SessionSpec spec, CancellationToken ct);
    Task<DeliveryReport> DeliverAsync(SessionHandle session, DeliveryRequest request, CancellationToken ct);
    Task SendKeysAsync(SessionHandle session, IReadOnlyList<NamedKey> keys, CancellationToken ct);
    Task<PaneSnapshot> CaptureAsync(SessionHandle session, CaptureRequest request, CancellationToken ct);
    Task<PaneStatus> GetStatusAsync(SessionHandle session, CancellationToken ct);
    Task<StopReport> StopAsync(SessionHandle session, StopRequest request, CancellationToken ct);
    Task<IReadOnlyList<SessionListing>> ListAsync(CancellationToken ct);
    Task<SessionHandle> AdoptAsync(SessionListing listing, CancellationToken ct);
    IReadOnlyList<string> GetAttachCommand(SessionHandle session, bool readOnlyMode = true);
    IAsyncEnumerable<SessionHostEvent> WatchAsync(CancellationToken ct);
}

public sealed record SessionSpec(
    string SeatId,
    string SeatAddress,
    string LaunchId,
    string Harness,
    IReadOnlyList<string> Argv,
    string WorkingDirectory,
    string SeatHome,
    IReadOnlyDictionary<string, string> Environment,
    TerminalSize Size,
    IOrphanProbe? OrphanProbe,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record SessionHandle(string SeatId, string LaunchId, string SessionName,
    string SessionId, string PaneId, int PanePid, ulong PaneStartTime, bool ReadOnly);

public sealed record DeliveryRequest(string Lead, string Body,
    SubmitKey Submit = SubmitKey.CtrlM, TimeSpan SubmitDelay = default, IDeliveryConfirmer? Confirmer = null);

public interface IDeliveryConfirmer
{
    Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct);
}

public abstract class DeliveryContext
{
    public abstract string DeliveryId { get; }
    public abstract DateTimeOffset SubmittedAt { get; }
    public abstract Task<PaneSnapshot> CaptureAsync(CaptureRequest request, CancellationToken ct);
    public abstract Task ResubmitAsync(string reason, CancellationToken ct);
}

public sealed record Confirmation(ConfirmationOutcome Outcome, string? TurnId);
public enum DeliveryStage { None, BufferLoaded, LeadTyped, BodyPasted, Submitted }
public sealed record DeliveryReport(string DeliveryId, DeliveryStage Stage, Confirmation Confirmation,
    int Resubmits, int BodyBytes, bool LineEndingsNormalized, SessionHostError? Error,
    string? ResubmitReason = null);
public enum NamedKey { Enter, Escape, Tab, Up, Down, Left, Right, CtrlC, CtrlD }
public sealed record CaptureRequest(int HistoryLines = 0, int MaxBytes = 1 << 20);
public sealed record PaneSnapshot(string Text, bool Truncated, int Lines, DateTimeOffset CapturedAt,
    TerminalSize Size, (int X, int Y)? Cursor, bool PaneDead);
public sealed record PaneStatus(PaneState State, int? ExitCode, int? Signal, string? Reason);
public enum PaneState { Alive, Exited, Missing, Unknown }
public sealed record StopRequest(TimeSpan Grace, GracefulStop? Graceful = null, TimeSpan? ChildGrace = null);
public abstract record GracefulStop
{
    public sealed record Signal(int Number) : GracefulStop;
    public sealed record Custom(Func<SessionHandle, CancellationToken, Task> StopAsync) : GracefulStop;
}

public sealed record StopReport(StopOutcome Outcome, int? ExitCode, int? Signal, int ChildrenSignalled,
    IReadOnlyList<int> Leftovers, SessionHostError? Error);
public interface IOrphanProbe { bool Matches(ProcessInfo process); }
public sealed record ProcessInfo(int Pid, int ParentPid, int SessionId, ulong StartTime, IReadOnlyList<string> Argv);
public sealed record SessionListing(string SessionName, string SessionId, string PaneId, int PanePid,
    bool PaneDead, int? ExitCode, int? Signal, SessionLabels? Labels, ListingClass Class, RegistryEntry? Registry);
public enum ListingClass { Managed, ManagedUnregistered, Foreign, Vanished }
public abstract record SessionHostEvent(SessionHandle Session, DateTimeOffset ObservedAt)
{
    public sealed record PaneExited(SessionHandle Session, DateTimeOffset ObservedAt, int? ExitCode, int? Signal)
        : SessionHostEvent(Session, ObservedAt);
    public sealed record SessionVanished(SessionHandle Session, DateTimeOffset ObservedAt)
        : SessionHostEvent(Session, ObservedAt);
}

public sealed record TerminalSize(int Columns, int Rows)
{
    public static TerminalSize Default { get; } = new(160, 45);
}

public enum SubmitKey { CtrlM, None }
public enum ConfirmationOutcome { Confirmed, Unconfirmed, NotRequested }
public enum StopOutcome { Stopped, Killed, NotRunning }
public enum SessionHostAvailability { Available, Unavailable }
public sealed record SessionHostInfo(string Kind, string? Version, SessionHostAvailability Availability,
    string? Reason, IReadOnlyList<string> Capabilities);
public enum SessionHostErrorCode { Unavailable, InvalidArgument, InputNotAllowed, PayloadTooLarge, AlreadyRunning,
    OrphanDetected, NotFound, Busy, TmuxTimeout, TmuxFailed, LeftoverProcesses }
public sealed record SessionHostError(SessionHostErrorCode Code, string Message, bool Retryable,
    IReadOnlyDictionary<string, string>? Metadata = null);
public sealed class SessionHostException : Exception
{
    public SessionHostException(SessionHostError error) : base(error.Message) => Error = error;
    public SessionHostError Error { get; }
    public SessionHostErrorCode Code => Error.Code;
}
public sealed record SessionLabels(int Schema, string Instance, string SeatId, string SeatAddress,
    string LaunchId, string Harness);
public sealed record RegistryExit(int? Code, int? Signal, DateTimeOffset ObservedAt, bool Reported);
public sealed record RegistryEntry(int Schema, string Instance, string State, string SeatId, string SeatAddress,
    string LaunchId, string Harness, string Socket, string SessionName, string? SessionId, string? PaneId,
    int? PanePid, ulong? PaneStartTime, DateTimeOffset CreatedAt, RegistryExit? Exit,
    IReadOnlyDictionary<string, string> Attributes);
