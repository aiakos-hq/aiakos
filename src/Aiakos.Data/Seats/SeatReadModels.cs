namespace Aiakos.Data.Seats;

/// <summary>A seat row projected for rig status listings.</summary>
public sealed record SeatStatusRow(
    Guid SeatId, string Address, string Rig, string Member, string Kind, string? Harness, string? Node, string Desired,
    string? Session, string? SessionReason, DateTime? SessionSince,
    string? Activity, string? ActivityDetail, string? ActivityReason, DateTime? ActivitySince,
    string? Resumability, string? ResumabilityReason, DateTime? ResumabilitySince,
    string? NativeSessionId, Guid? LaunchId, string? LaunchOutcome, string? LaunchDecision,
    bool SpecDrift, string? PendingOp, string? LastDeliveryOutcome,
    int? ContextUsedPercent, string? Model, DateTime? LastEventAt,
    long OpenFindings, string? WorstSeverity);

/// <summary>A seat and its current launch, transitions, open findings, and recent deliveries.</summary>
public sealed record SeatDetail(
    SeatStatusRow Seat,
    SeatLaunchRow? Launch,
    IReadOnlyList<SeatTransitionRow> Transitions,
    IReadOnlyList<SeatFindingRow> Findings,
    IReadOnlyList<SeatCommandRow> Deliveries);

/// <summary>A launch row with its decision and outcome evidence, excluding the seat token hash.</summary>
public sealed record SeatLaunchRow(
    Guid LaunchId, Guid SeatId, string Mode, string Decision, string DecidedBy, string? DecisionNote,
    string NodeName, string SpecHash, string BindingHash, string? Outcome, string? OutcomeReason,
    string? ObservedSessionId, int? ExitCode, int? ExitSignal, string? Evidence,
    DateTime RequestedAt, DateTime? OutcomeAt, DateTime? EndedAt, string? EndReason);

/// <summary>A command row with its result, excluding the command payload.</summary>
public sealed record SeatCommandRow(
    Guid CommandId, Guid SeatId, Guid? LaunchId, string Kind, string Status, string? Outcome,
    string? ErrorReason, bool Forced, string? TurnId, string? Result, string RequestedBy,
    DateTime CreatedAt, DateTime? SentAt, DateTime? CompletedAt);

/// <summary>A recorded change to one of a seat's reported state axes.</summary>
public sealed record SeatTransitionRow(
    string Axis, bool Reported, string FromValue, string ToValue, string? Reason, string CauseType,
    Guid? LaunchId, DateTime At);

/// <summary>An open or resolved finding recorded for a seat.</summary>
public sealed record SeatFindingRow(
    Guid FindingId, string Kind, string Severity, string Summary, int Occurrences,
    DateTime FirstSeenAt, DateTime LastSeenAt, Guid? LaunchId);
