using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

public static class SeatStateMachine
{
    private static readonly IReadOnlyList<FindingChange> NoFindings = Array.Empty<FindingChange>();
    private static readonly IReadOnlyList<SeatTransition> NoTransitions = Array.Empty<SeatTransition>();
    private static readonly IReadOnlyList<SeatEffect> NoEffects = Array.Empty<SeatEffect>();

    public static SeatStep Apply(SeatState state, SeatInput input, IHarnessStateProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);

        if (input is EventReceived received)
            return ApplyEvent(state, received, profile, now);

        if (input is CommandDispatchFailed dispatchFailed)
        {
            var rule = dispatchFailed.Kind switch
            {
                SeatCommandKind.Start when state.KnownSession == SessionValue.Starting =>
                    WithFindings(ApplySession(state, SessionValue.Absent, null, "S9", now),
                        Open(SeatVocabulary.FindingLaunchRejected)),
                SeatCommandKind.Stop when state.KnownSession is SessionValue.Starting or SessionValue.Present => ApplySession(state, SessionValue.Unknown,
                    SeatVocabulary.SessionReasonStopFailed, "S4", now),
                _ => null
            };
            return rule ?? Unchanged(state);
        }

        if (input is LaunchWatchdogFired && state.KnownSession == SessionValue.Starting)
        {
            var step = ApplySession(state, SessionValue.Unknown,
                SeatVocabulary.SessionReasonLaunchResultMissing, "S10", now);
            return WithFindings(step, Open(SeatVocabulary.FindingLaunchUnconfirmed));
        }

        return Unchanged(state);
    }

    private static SeatStep ApplyEvent(SeatState state, EventReceived input, IHarnessStateProfile profile,
        DateTimeOffset now)
    {
        var isCurrentLaunch = state.Launch is not null && input.LaunchId == state.Launch.LaunchId;
        if (!isCurrentLaunch)
        {
            var staleFindings = input.Body is HarnessBody harness && IsOrphanHarness(harness, profile)
                ? Open(SeatVocabulary.FindingOrphanHarness)
                : NoFindings;
            return new SeatStep(state with { LastEventAt = now }, null, EventDisposition.StaleLaunch,
                NoTransitions, staleFindings, NoEffects);
        }

        SeatStep result = input.Body switch
        {
            HarnessBody harness => HarnessEvent(state, harness, profile, now),
            SessionObservedBody observed when !observed.MatchesExpected => SessionIdMismatch(state,
                state.Resumability == ResumabilityValue.None ? EventDisposition.Evidence : EventDisposition.Applied, now),
            LaunchResultBody launch => LaunchResult(state, launch, now),
            StartNotCompletedBody start => StartNotCompleted(state, start, now),
            StopResultBody stop => StopResult(state, stop, now),
            StopNotCompletedBody stop => StopNotCompleted(state, stop, now),
            ProcessExitedBody => ProcessExited(state, now),
            CaptureBody capture when capture.PaneDead && state.KnownSession == SessionValue.Present =>
                new SeatStep(state, null, EventDisposition.Evidence, NoTransitions,
                    Open(SeatVocabulary.FindingSourcesDisagree), NoEffects),
            _ => new SeatStep(state, null, EventDisposition.Evidence, NoTransitions, NoFindings, NoEffects)
        };
        var findings = result.Findings;
        if (input.Body is HarnessBody)
            findings = [.. findings, new FindingChange(SeatVocabulary.FindingActivityStale, false)];

        return result with
        {
            State = result.State with { LastEventAt = now },
            Disposition = result.Disposition ?? EventDisposition.Applied,
            Findings = findings
        };
    }

    private static SeatStep HarnessEvent(SeatState state, HarnessBody input, IHarnessStateProfile profile,
        DateTimeOffset now)
    {
        var kind = Enum.IsDefined(input.Kind) && input.Kind != HarnessEventKind.Unspecified
            ? input.Kind
            : HarnessEventKind.Other;

        if (kind == HarnessEventKind.SessionEnded)
        {
            if (!state.ReadinessSeen)
                return EmptyEvent(state, EventDisposition.Orphan);
            if (state.KnownSession is SessionValue.Present or SessionValue.Unknown)
                return ApplySession(state, SessionValue.Exited, null, "S12", now);
            return EmptyEvent(state, state.KnownSession == SessionValue.Exited
                ? EventDisposition.Applied
                : EventDisposition.Evidence);
        }

        if (!profile.IsReadiness(kind, input.Attributes))
            return EmptyEvent(state, kind is HarnessEventKind.Telemetry or HarnessEventKind.Other
                ? EventDisposition.Applied
                : EventDisposition.Evidence);

        if (string.Equals(input.NativeSessionId, state.NativeSessionId, StringComparison.Ordinal))
            return Readiness(state, now);

        if (profile.IsSessionRotation(kind, input.Attributes) &&
            input.Attributes.TryGetValue("previous_session_id", out var previousSessionId) &&
            string.Equals(previousSessionId, state.NativeSessionId, StringComparison.Ordinal) &&
            profile.IsValidNativeSessionId(input.NativeSessionId))
            return RotateSession(state, input.NativeSessionId, previousSessionId, now);

        return SessionIdMismatch(state, state.Resumability == ResumabilityValue.None
            ? EventDisposition.Evidence
            : EventDisposition.Applied, now);
    }

    private static SeatStep Readiness(SeatState state, DateTimeOffset now)
    {
        var ready = state with { ReadinessSeen = true };
        if (state.KnownSession == SessionValue.Absent)
            return EmptyEvent(ready, EventDisposition.Evidence);

        if (state.KnownSession == SessionValue.Exited)
        {
            var disagree = ApplySession(ready, SessionValue.Unknown, SeatVocabulary.SessionReasonSourcesDisagree,
                "S11", now);
            return WithFindings(disagree, Open(SeatVocabulary.FindingSourcesDisagree));
        }

        var step = ApplySession(ready, SessionValue.Present, null, "S11", now,
            activityOnPresent: ActivityValue.Idle, activityRule: "A1");
        return WithFindings(step, ResolveLaunchFindings());
    }

    private static SeatStep SessionIdMismatch(SeatState state, EventDisposition disposition, DateTimeOffset now)
    {
        if (state.Resumability == ResumabilityValue.None)
            return EmptyEvent(state, disposition);

        var next = state;
        IReadOnlyList<SeatTransition> transitions = NoTransitions;
        if (state.Resumability != ResumabilityValue.Unknown)
        {
            next = state with
            {
                Resumability = ResumabilityValue.Unknown,
                ResumabilityReason = SeatVocabulary.ResumabilityReasonSessionIdMismatch,
                ResumabilitySince = now
            };
            transitions = [new SeatTransition("resumability", true,
                SeatVocabulary.ToStored(state.Resumability), SeatVocabulary.ToStored(ResumabilityValue.Unknown),
                SeatVocabulary.ResumabilityReasonSessionIdMismatch, "U7")];
        }

        return new SeatStep(next, null, disposition, transitions,
            Open(SeatVocabulary.FindingSessionIdMismatch), NoEffects);
    }

    private static SeatStep RotateSession(SeatState state, string nativeSessionId, string previousSessionId,
        DateTimeOffset now)
    {
        var next = state with { NativeSessionId = nativeSessionId, ReadinessSeen = true };
        var transitions = new List<SeatTransition>();
        if (state.Resumability != ResumabilityValue.None &&
            (state.Resumability != ResumabilityValue.FreshOnly || state.ResumabilityReason is not null))
        {
            next = next with
            {
                Resumability = ResumabilityValue.FreshOnly,
                ResumabilityReason = null,
                ResumabilitySince = now
            };
            transitions.Add(new SeatTransition("resumability", true,
                SeatVocabulary.ToStored(state.Resumability), SeatVocabulary.ToStored(ResumabilityValue.FreshOnly),
                null, "U8"));
        }

        if (state.KnownSession == SessionValue.Present &&
            (state.KnownActivity != ActivityValue.Idle || state.KnownActivityReason is not null || state.KnownActivityDetail is not null))
        {
            next = next with
            {
                KnownActivity = ActivityValue.Idle,
                KnownActivityReason = null,
                KnownActivityDetail = null,
                Activity = state.Overlay is null ? ActivityValue.Idle : state.Activity,
                ActivityReason = state.Overlay is null ? null : state.ActivityReason,
                ActivityDetail = state.Overlay is null ? null : state.ActivityDetail,
                ActivitySince = now
            };
            transitions.Add(new SeatTransition("activity", state.Overlay is null,
                SeatVocabulary.ToStored(state.KnownActivity), SeatVocabulary.ToStored(ActivityValue.Idle), null, "A1"));
        }

        return new SeatStep(next, null, EventDisposition.Applied, transitions,
            [new FindingChange(SeatVocabulary.FindingSessionIdMismatch, false)],
            [new AdoptRotatedSession(nativeSessionId, previousSessionId)]);
    }

    private static SeatStep LaunchResult(SeatState state, LaunchResultBody input, DateTimeOffset now) => input.Outcome switch
    {
        LaunchOutcome.Ready => Ready(state, now),
        LaunchOutcome.Failed => Failed(state, now),
        _ => UnknownLaunch(state, now)
    };

    private static SeatStep Ready(SeatState state, DateTimeOffset now)
    {
        if (state.KnownSession is SessionValue.Starting or SessionValue.Unknown)
        {
            var step = ApplySession(state, SessionValue.Present, null, "S5", now,
                activityOnPresent: ActivityValue.Unknown,
                activityReason: SeatVocabulary.SessionReasonSourcesDisagree);
            var findings = new List<FindingChange>();
            if (state.KnownSession == SessionValue.Starting && !state.ReadinessSeen)
                findings.Add(new FindingChange(SeatVocabulary.FindingSourcesDisagree, true));
            findings.AddRange(ResolveLaunchFindings());
            return WithFindings(step, findings);
        }

        if (state.KnownSession == SessionValue.Exited)
        {
            var step = ApplySession(state, SessionValue.Unknown, SeatVocabulary.SessionReasonSourcesDisagree, "S5", now);
            return WithFindings(step, Open(SeatVocabulary.FindingSourcesDisagree));
        }

        if (state.KnownSession == SessionValue.Present)
            return WithFindings(EmptyEvent(state), ResolveLaunchFindings());
        return EmptyEvent(state, EventDisposition.Evidence);
    }

    private static SeatStep Failed(SeatState state, DateTimeOffset now)
    {
        if (state.KnownSession == SessionValue.Starting)
            return ExitFromLaunchFailure(state, now);
        if (state.KnownSession == SessionValue.Present)
        {
            var step = ApplySession(state, SessionValue.Unknown, SeatVocabulary.SessionReasonSourcesDisagree, "S6", now);
            return WithFindings(step, Open(SeatVocabulary.FindingSourcesDisagree));
        }
        if (state.KnownSession == SessionValue.Unknown)
            return ExitFromLaunchFailure(state, now);
        return EmptyEvent(state, state.KnownSession == SessionValue.Absent
            ? EventDisposition.Evidence
            : EventDisposition.Applied);
    }

    private static SeatStep ExitFromLaunchFailure(SeatState state, DateTimeOffset now)
    {
        var step = ApplySession(state, SessionValue.Exited, null, "S6", now);
        if (state.Desired == SeatDesired.Up && state.Launch?.StopRequested != true)
            return WithFindings(step, Open(SeatVocabulary.FindingUnexpectedExit));
        return step;
    }

    private static SeatStep UnknownLaunch(SeatState state, DateTimeOffset now)
    {
        if (state.KnownSession == SessionValue.Starting)
        {
            var step = ApplySession(state, SessionValue.Unknown,
                SeatVocabulary.SessionReasonLaunchUnconfirmed, "S7", now);
            return WithFindings(step, Open(SeatVocabulary.FindingLaunchUnconfirmed));
        }
        if (state.KnownSession == SessionValue.Present)
        {
            var step = ApplySession(state, SessionValue.Unknown,
                SeatVocabulary.SessionReasonSourcesDisagree, "S7", now);
            return WithFindings(step, Open(SeatVocabulary.FindingSourcesDisagree));
        }
        return EmptyEvent(state, state.KnownSession == SessionValue.Absent
            ? EventDisposition.Evidence
            : EventDisposition.Applied);
    }

    private static SeatStep StartNotCompleted(SeatState state, StartNotCompletedBody input, DateTimeOffset now)
    {
        if (input.Status == CommandStatus.Completed)
            return EmptyEvent(state, EventDisposition.Evidence);
        if (input.Status == CommandStatus.TimedOut || input.Status is CommandStatus.Unspecified ||
            !Enum.IsDefined(input.Status))
        {
            if (state.KnownSession != SessionValue.Starting)
                return EmptyEvent(state, state.KnownSession == SessionValue.Absent
                    ? EventDisposition.Evidence
                    : EventDisposition.Applied);
            var step = ApplySession(state, SessionValue.Unknown,
                SeatVocabulary.SessionReasonLaunchUnconfirmed, "S10", now);
            return WithFindings(step, Open(SeatVocabulary.FindingLaunchUnconfirmed));
        }
        if (input.Status == CommandStatus.Rejected &&
            input.Reason is SeatVocabulary.RejectionSeatAlreadyRunning or SeatVocabulary.RejectionOrphanHarnessDetected)
        {
            if (state.KnownSession != SessionValue.Starting)
                return EmptyEvent(state, EventDisposition.Evidence);
            var step = ApplySession(state, SessionValue.Unknown,
                SeatVocabulary.SessionReasonOrphanOrRunning, "S8", now);
            return WithFindings(step, Open(SeatVocabulary.FindingOrphanHarness));
        }
        if (input.Status is CommandStatus.Rejected or CommandStatus.Failed &&
            state.KnownSession == SessionValue.Starting)
        {
            var step = ApplySession(state, SessionValue.Absent, null, "S9", now);
            return WithFindings(step, Open(SeatVocabulary.FindingLaunchRejected));
        }
        if (input.Status is CommandStatus.Rejected or CommandStatus.Failed)
            return EmptyEvent(state, EventDisposition.Evidence);
        return EmptyEvent(state, state.KnownSession == SessionValue.Absent
            ? EventDisposition.Evidence
            : EventDisposition.Applied);
    }

    private static SeatStep StopResult(SeatState state, StopResultBody input, DateTimeOffset now)
    {
        if (input.Outcome is StopOutcome.Stopped or StopOutcome.Killed or StopOutcome.NotRunning)
        {
            if (state.KnownSession == SessionValue.Absent)
                return EmptyEvent(state, EventDisposition.Evidence);
            return ApplySession(state, SessionValue.Absent, null, "S3", now);
        }
        if (state.KnownSession is SessionValue.Starting or SessionValue.Present)
            return ApplySession(state, SessionValue.Unknown, SeatVocabulary.SessionReasonStopFailed, "S4", now);
        return EmptyEvent(state, state.KnownSession == SessionValue.Absent
            ? EventDisposition.Evidence
            : EventDisposition.Applied);
    }

    private static SeatStep StopNotCompleted(SeatState state, StopNotCompletedBody input, DateTimeOffset now)
    {
        if (input.Status == CommandStatus.Completed)
            return EmptyEvent(state, EventDisposition.Evidence);
        if (state.KnownSession is SessionValue.Starting or SessionValue.Present)
            return ApplySession(state, SessionValue.Unknown, SeatVocabulary.SessionReasonStopFailed, "S4", now);
        return EmptyEvent(state, state.KnownSession == SessionValue.Absent
            ? EventDisposition.Evidence
            : EventDisposition.Applied);
    }

    private static SeatStep ProcessExited(SeatState state, DateTimeOffset now)
    {
        if (state.KnownSession == SessionValue.Absent)
            return EmptyEvent(state, EventDisposition.Evidence);
        if (state.KnownSession == SessionValue.Exited)
            return EmptyEvent(state);
        var step = ApplySession(state, SessionValue.Exited, null, "S14", now);
        if (state.Desired == SeatDesired.Up && state.Launch?.StopRequested != true)
            return WithFindings(step, Open(SeatVocabulary.FindingUnexpectedExit));
        return step;
    }

    private static SeatStep ApplySession(SeatState state, SessionValue session, string? reason, string rule,
        DateTimeOffset now, ActivityValue? activityOnPresent = null, string? activityReason = null,
        string activityRule = "R10")
    {
        var transitions = new List<SeatTransition>();
        var changed = state.KnownSession != session || state.KnownSessionReason != reason;
        var next = state;
        if (changed)
        {
            next = next with
            {
                KnownSession = session,
                KnownSessionReason = reason,
                Session = state.Overlay is null ? session : state.Session,
                SessionReason = state.Overlay is null ? reason : state.SessionReason,
                SessionSince = now
            };
            transitions.Add(new SeatTransition("session", state.Overlay is null,
                SeatVocabulary.ToStored(state.KnownSession), SeatVocabulary.ToStored(session), reason, rule));
        }

        ActivityValue? derivedActivity = session switch
        {
            SessionValue.Absent or SessionValue.Exited => ActivityValue.None,
            SessionValue.Starting => ActivityValue.Unknown,
            SessionValue.Unknown => ActivityValue.Unknown,
            SessionValue.Present when activityOnPresent.HasValue => activityOnPresent.Value,
            _ => null
        };
        var derivedReason = session switch
        {
            SessionValue.Starting => SeatVocabulary.ActivityReasonNotReady,
            SessionValue.Unknown => SeatVocabulary.ActivityReasonSessionUnknown,
            SessionValue.Present when activityOnPresent == ActivityValue.Unknown => activityReason,
            _ => null
        };
        if (derivedActivity is { } activity)
        {
            var activityChanged = state.KnownActivity != activity || state.KnownActivityReason != derivedReason ||
                activity != ActivityValue.Unknown && state.KnownActivityDetail is not null;
            if (activityChanged)
            {
                next = next with
                {
                    KnownActivity = activity,
                    KnownActivityReason = derivedReason,
                    KnownActivityDetail = null,
                    Activity = state.Overlay is null ? activity : state.Activity,
                    ActivityReason = state.Overlay is null ? derivedReason : state.ActivityReason,
                    ActivityDetail = state.Overlay is null ? null : state.ActivityDetail,
                    ActivitySince = now
                };
                transitions.Add(new SeatTransition("activity", state.Overlay is null,
                    SeatVocabulary.ToStored(state.KnownActivity), SeatVocabulary.ToStored(activity), derivedReason, activityRule));
            }
        }
        return new SeatStep(next, null, null, transitions, NoFindings, NoEffects);
    }

    private static SeatStep EmptyEvent(SeatState state, EventDisposition disposition = EventDisposition.Applied) =>
        new(state, null, disposition, NoTransitions, NoFindings, NoEffects);

    private static SeatStep Unchanged(SeatState state) =>
        new(state, null, null, NoTransitions, NoFindings, NoEffects);

    private static IReadOnlyList<FindingChange> Open(string kind) => [new FindingChange(kind, true)];

    private static IReadOnlyList<FindingChange> ResolveLaunchFindings() =>
    [
        new FindingChange(SeatVocabulary.FindingLaunchUnconfirmed, false),
        new FindingChange(SeatVocabulary.FindingLaunchRejected, false),
        new FindingChange(SeatVocabulary.FindingLaunchFailed, false),
        new FindingChange(SeatVocabulary.FindingUnexpectedExit, false)
    ];

    private static bool IsOrphanHarness(HarnessBody input, IHarnessStateProfile profile) =>
        profile.IsReadiness(input.Kind, input.Attributes) || input.Kind is HarnessEventKind.PromptSubmitted or
            HarnessEventKind.ToolStarted or HarnessEventKind.ToolFinished or HarnessEventKind.InputRequested;

    private static SeatStep WithFindings(SeatStep step, IReadOnlyList<FindingChange> findings) =>
        step with { Findings = findings };

    private static SeatStep WithFindings(SeatStep step, List<FindingChange> findings) =>
        step with { Findings = findings };
}
