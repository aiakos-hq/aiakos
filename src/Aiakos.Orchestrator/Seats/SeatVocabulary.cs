using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

public static class SeatVocabulary
{
    public const string SessionReasonNodeLinkLost = "node-link-lost";
    public const string SessionReasonOrchestratorRestarted = "orchestrator-restarted";
    public const string SessionReasonLaunchUnconfirmed = "launch-unconfirmed";
    public const string SessionReasonLaunchResultMissing = "launch-result-missing";
    public const string SessionReasonStopFailed = "stop-failed";
    public const string SessionReasonOrphanOrRunning = "orphan-or-running";
    public const string SessionReasonInventoryMissing = "inventory-missing";
    public const string SessionReasonSourcesDisagree = "sources-disagree";
    public const string ActivityReasonNotReady = "not-ready";
    public const string ActivityReasonSessionUnknown = "session-unknown";
    public const string ActivityReasonObservationGap = "observation-gap";
    public const string ActivityReasonQuietTimeout = "quiet-timeout";
    public const string ResumabilityReasonObservationGap = "observation-gap";
    public const string ResumabilityReasonFreshRelaunchFailed = "fresh-relaunch-failed";
    public const string ResumabilityReasonSessionIdMismatch = "session-id-mismatch";
    public const string ResumabilityReasonContradictingEvidence = "contradicting-evidence";

    public const string ActivityDetailCompacting = "compacting";
    public const string ActivityDetailRetrying = "retrying";
    public const string ActivityDetailToolPrefix = "tool:";

    public const string FindingSourcesDisagree = "sources-disagree";
    public const string FindingLaunchUnconfirmed = "launch-unconfirmed";
    public const string FindingLaunchRejected = "launch-rejected";
    public const string FindingLaunchFailed = "launch-failed";
    public const string FindingResumeLost = "resume-lost";
    public const string FindingOrphanHarness = "orphan-harness";
    public const string FindingInventoryMismatch = "inventory-mismatch";
    public const string FindingSessionIdMismatch = "session-id-mismatch";
    public const string FindingObservationGap = "observation-gap";
    public const string FindingActivityStale = "activity-stale";
    public const string FindingTurnFailed = "turn-failed";
    public const string FindingDeliveryUnconfirmed = "delivery-unconfirmed";
    public const string FindingUnexpectedExit = "unexpected-exit";
    public const string FindingStateUnknownProlonged = "state-unknown-prolonged";
    public const string FindingNodeNotConnected = "node-not-connected";
    public const string FindingActorStopped = "actor-stopped";

    public const string DecisionNewSession = "new-session";
    public const string DecisionNoConversationYet = "no-conversation-yet";
    public const string DecisionFreshExplicit = "fresh-explicit";
    public const string DecisionResume = "resume";
    public const string DecisionResumeUnverified = "resume-unverified";
    public const string DecisionHarnessCleared = "harness-cleared";

    public const string RejectionSeatStateUnknown = "SEAT_STATE_UNKNOWN";
    public const string RejectionResumeLost = "RESUME_LOST";
    public const string RejectionSeatAlreadyRunning = "SEAT_ALREADY_RUNNING";
    public const string RejectionOrphanHarnessDetected = "ORPHAN_HARNESS_DETECTED";
    public const string RejectionSeatNotReady = "SEAT_NOT_READY";
    public const string RejectionSeatBusy = "SEAT_BUSY";
    public const string RejectionSeatStopping = "SEAT_STOPPING";
    public const string RejectionSessionIdMismatch = "SESSION_ID_MISMATCH";
    public const string LaunchReasonReadyTimeout = "READY_TIMEOUT";
    public const string LaunchReasonStopped = "STOPPED";
    public const string LaunchReasonResumeSessionNotFound = "RESUME_SESSION_NOT_FOUND";
    public const string LaunchReasonHarnessExited = "HARNESS_EXITED";

    public static string ToStored(SessionValue value) => value switch
    {
        SessionValue.Absent => "absent",
        SessionValue.Starting => "starting",
        SessionValue.Present => "present",
        SessionValue.Exited => "exited",
        SessionValue.Unknown => "unknown",
        _ => "unknown"
    };

    public static string ToStored(ActivityValue value) => value switch
    {
        ActivityValue.None => "none",
        ActivityValue.Idle => "idle",
        ActivityValue.Working => "working",
        ActivityValue.NeedsInput => "needs-input",
        ActivityValue.Unknown => "unknown",
        _ => "unknown"
    };

    public static string ToStored(ResumabilityValue value) => value switch
    {
        ResumabilityValue.None => "none",
        ResumabilityValue.FreshOnly => "fresh-only",
        ResumabilityValue.Resumable => "resumable",
        ResumabilityValue.Lost => "lost",
        ResumabilityValue.Unknown => "unknown",
        _ => "unknown"
    };

    public static string ToStored(SeatOverlay value) => value switch
    {
        SeatOverlay.NodeLinkLost => "node-link-lost",
        SeatOverlay.OrchestratorRestarted => "orchestrator-restarted",
        _ => "unknown"
    };

    public static string ToStored(EventDisposition value) => value switch
    {
        EventDisposition.Applied => "applied",
        EventDisposition.Late => "late",
        EventDisposition.StaleLaunch => "stale-launch",
        EventDisposition.Orphan => "orphan",
        EventDisposition.Evidence => "evidence",
        EventDisposition.Duplicate => throw new ArgumentException("Duplicate is not a stored disposition.", nameof(value)),
        _ => "unknown"
    };

    public static string ToStored(HarnessEventKind value) => value switch
    {
        HarnessEventKind.Other => "other",
        HarnessEventKind.SessionStarted => "session-started",
        HarnessEventKind.PromptSubmitted => "prompt-submitted",
        HarnessEventKind.Active => "active",
        HarnessEventKind.ToolStarted => "tool-started",
        HarnessEventKind.ToolFinished => "tool-finished",
        HarnessEventKind.InputRequested => "input-requested",
        HarnessEventKind.InputResolved => "input-resolved",
        HarnessEventKind.CompactionStarted => "compaction-started",
        HarnessEventKind.Compacted => "compacted",
        HarnessEventKind.TurnEnded => "turn-ended",
        HarnessEventKind.TurnFailed => "turn-failed",
        HarnessEventKind.Retrying => "retrying",
        HarnessEventKind.SessionEnded => "session-ended",
        HarnessEventKind.Telemetry => "telemetry",
        _ => "unknown"
    };

    public static string ToStored(LaunchMode value) => value switch
    {
        LaunchMode.Fresh => "fresh",
        LaunchMode.Resume => "resume",
        LaunchMode.Fork => "fork",
        _ => "unknown"
    };

    public static string ToStored(LaunchOutcome value) => value switch
    {
        LaunchOutcome.Ready => "ready",
        LaunchOutcome.Failed => "failed",
        LaunchOutcome.Unknown => "unknown",
        _ => "unknown"
    };

    public static string ToStored(StopOutcome value) => value switch
    {
        StopOutcome.Stopped => "stopped",
        StopOutcome.Killed => "killed",
        StopOutcome.NotRunning => "not-running",
        _ => "unknown"
    };

    public static string ToStored(DeliveryOutcome value) => value switch
    {
        DeliveryOutcome.Confirmed => "confirmed",
        DeliveryOutcome.SubmittedUnconfirmed => "submitted-unconfirmed",
        DeliveryOutcome.NotDelivered => "not-delivered",
        _ => "unknown"
    };

    public static string ToStored(CommandStatus value) => value switch
    {
        CommandStatus.Completed => "completed",
        CommandStatus.Rejected => "rejected",
        CommandStatus.Failed => "failed",
        CommandStatus.TimedOut => "timed-out",
        _ => "unknown"
    };
}
