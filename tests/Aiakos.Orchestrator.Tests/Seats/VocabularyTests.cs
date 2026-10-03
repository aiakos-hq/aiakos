using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class VocabularyTests
{
    [Fact]
    public void CreatesAnInitialAbsentDownState()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        var state = SeatState.Initial(now);

        Assert.Equal(SessionValue.Absent, state.Session);
        Assert.Equal(ActivityValue.None, state.Activity);
        Assert.Equal(ResumabilityValue.None, state.Resumability);
        Assert.Equal(SeatDesired.Down, state.Desired);
        Assert.Equal(SessionValue.Absent, state.KnownSession);
        Assert.Equal(ActivityValue.None, state.KnownActivity);
        Assert.Equal(now, state.SessionSince);
        Assert.Equal(now, state.ActivitySince);
        Assert.Equal(now, state.ResumabilitySince);
        Assert.Equal(1, state.NextSeq);
    }

    [Fact]
    public void StoresSeatValuesUsingExactLowerKebabStrings()
    {
        Assert.Equal("absent", SeatVocabulary.ToStored(SessionValue.Absent));
        Assert.Equal("starting", SeatVocabulary.ToStored(SessionValue.Starting));
        Assert.Equal("present", SeatVocabulary.ToStored(SessionValue.Present));
        Assert.Equal("exited", SeatVocabulary.ToStored(SessionValue.Exited));
        Assert.Equal("unknown", SeatVocabulary.ToStored(SessionValue.Unknown));
        Assert.Equal("none", SeatVocabulary.ToStored(ActivityValue.None));
        Assert.Equal("idle", SeatVocabulary.ToStored(ActivityValue.Idle));
        Assert.Equal("working", SeatVocabulary.ToStored(ActivityValue.Working));
        Assert.Equal("needs-input", SeatVocabulary.ToStored(ActivityValue.NeedsInput));
        Assert.Equal("unknown", SeatVocabulary.ToStored(ActivityValue.Unknown));
        Assert.Equal("none", SeatVocabulary.ToStored(ResumabilityValue.None));
        Assert.Equal("fresh-only", SeatVocabulary.ToStored(ResumabilityValue.FreshOnly));
        Assert.Equal("resumable", SeatVocabulary.ToStored(ResumabilityValue.Resumable));
        Assert.Equal("lost", SeatVocabulary.ToStored(ResumabilityValue.Lost));
        Assert.Equal("unknown", SeatVocabulary.ToStored(ResumabilityValue.Unknown));
        Assert.Equal("node-link-lost", SeatVocabulary.ToStored(SeatOverlay.NodeLinkLost));
        Assert.Equal("orchestrator-restarted", SeatVocabulary.ToStored(SeatOverlay.OrchestratorRestarted));
        Assert.Equal("applied", SeatVocabulary.ToStored(EventDisposition.Applied));
        Assert.Equal("late", SeatVocabulary.ToStored(EventDisposition.Late));
        Assert.Equal("stale-launch", SeatVocabulary.ToStored(EventDisposition.StaleLaunch));
        Assert.Equal("orphan", SeatVocabulary.ToStored(EventDisposition.Orphan));
        Assert.Equal("evidence", SeatVocabulary.ToStored(EventDisposition.Evidence));
    }

    [Fact]
    public void StoresEveryDefinedProtoEnumValueUsingExactLowerKebabStrings()
    {
        Assert.Equal("unknown", SeatVocabulary.ToStored(HarnessEventKind.Unspecified));
        Assert.Equal("other", SeatVocabulary.ToStored(HarnessEventKind.Other));
        Assert.Equal("session-started", SeatVocabulary.ToStored(HarnessEventKind.SessionStarted));
        Assert.Equal("prompt-submitted", SeatVocabulary.ToStored(HarnessEventKind.PromptSubmitted));
        Assert.Equal("active", SeatVocabulary.ToStored(HarnessEventKind.Active));
        Assert.Equal("tool-started", SeatVocabulary.ToStored(HarnessEventKind.ToolStarted));
        Assert.Equal("tool-finished", SeatVocabulary.ToStored(HarnessEventKind.ToolFinished));
        Assert.Equal("input-requested", SeatVocabulary.ToStored(HarnessEventKind.InputRequested));
        Assert.Equal("input-resolved", SeatVocabulary.ToStored(HarnessEventKind.InputResolved));
        Assert.Equal("compaction-started", SeatVocabulary.ToStored(HarnessEventKind.CompactionStarted));
        Assert.Equal("compacted", SeatVocabulary.ToStored(HarnessEventKind.Compacted));
        Assert.Equal("turn-ended", SeatVocabulary.ToStored(HarnessEventKind.TurnEnded));
        Assert.Equal("turn-failed", SeatVocabulary.ToStored(HarnessEventKind.TurnFailed));
        Assert.Equal("retrying", SeatVocabulary.ToStored(HarnessEventKind.Retrying));
        Assert.Equal("session-ended", SeatVocabulary.ToStored(HarnessEventKind.SessionEnded));
        Assert.Equal("telemetry", SeatVocabulary.ToStored(HarnessEventKind.Telemetry));

        Assert.Equal("unknown", SeatVocabulary.ToStored(LaunchMode.Unspecified));
        Assert.Equal("fresh", SeatVocabulary.ToStored(LaunchMode.Fresh));
        Assert.Equal("resume", SeatVocabulary.ToStored(LaunchMode.Resume));
        Assert.Equal("fork", SeatVocabulary.ToStored(LaunchMode.Fork));
        Assert.Equal("unknown", SeatVocabulary.ToStored(LaunchOutcome.Unspecified));
        Assert.Equal("ready", SeatVocabulary.ToStored(LaunchOutcome.Ready));
        Assert.Equal("failed", SeatVocabulary.ToStored(LaunchOutcome.Failed));
        Assert.Equal("unknown", SeatVocabulary.ToStored(LaunchOutcome.Unknown));
        Assert.Equal("unknown", SeatVocabulary.ToStored(StopOutcome.Unspecified));
        Assert.Equal("stopped", SeatVocabulary.ToStored(StopOutcome.Stopped));
        Assert.Equal("killed", SeatVocabulary.ToStored(StopOutcome.Killed));
        Assert.Equal("not-running", SeatVocabulary.ToStored(StopOutcome.NotRunning));
        Assert.Equal("unknown", SeatVocabulary.ToStored(DeliveryOutcome.Unspecified));
        Assert.Equal("confirmed", SeatVocabulary.ToStored(DeliveryOutcome.Confirmed));
        Assert.Equal("submitted-unconfirmed", SeatVocabulary.ToStored(DeliveryOutcome.SubmittedUnconfirmed));
        Assert.Equal("not-delivered", SeatVocabulary.ToStored(DeliveryOutcome.NotDelivered));
        Assert.Equal("unknown", SeatVocabulary.ToStored(CommandStatus.Unspecified));
        Assert.Equal("completed", SeatVocabulary.ToStored(CommandStatus.Completed));
        Assert.Equal("rejected", SeatVocabulary.ToStored(CommandStatus.Rejected));
        Assert.Equal("failed", SeatVocabulary.ToStored(CommandStatus.Failed));
        Assert.Equal("timed-out", SeatVocabulary.ToStored(CommandStatus.TimedOut));
    }

    [Fact]
    public void StoresOnlyDistinctDefinedNonUnspecifiedProtoValues()
    {
        AssertDistinct(HarnessEventKind.Unspecified, SeatVocabulary.ToStored);
        AssertDistinct(LaunchMode.Unspecified, SeatVocabulary.ToStored);
        AssertDistinct(LaunchOutcome.Unspecified, SeatVocabulary.ToStored);
        AssertDistinct(StopOutcome.Unspecified, SeatVocabulary.ToStored);
        AssertDistinct(DeliveryOutcome.Unspecified, SeatVocabulary.ToStored);
        AssertDistinct(CommandStatus.Unspecified, SeatVocabulary.ToStored);
    }

    [Fact]
    public void StoresUnspecifiedAndUndefinedProtoValuesAsUnknown()
    {
        Assert.Equal("unknown", SeatVocabulary.ToStored((HarnessEventKind)12345));
        Assert.Equal("unknown", SeatVocabulary.ToStored((LaunchMode)12345));
        Assert.Equal("unknown", SeatVocabulary.ToStored((LaunchOutcome)12345));
        Assert.Equal("unknown", SeatVocabulary.ToStored((StopOutcome)12345));
        Assert.Equal("unknown", SeatVocabulary.ToStored((DeliveryOutcome)12345));
        Assert.Equal("unknown", SeatVocabulary.ToStored((CommandStatus)12345));
    }

    [Fact]
    public void DoesNotStoreDuplicateDisposition()
    {
        Assert.Throws<ArgumentException>(() => SeatVocabulary.ToStored(EventDisposition.Duplicate));
    }

    private static void AssertDistinct<TEnum>(TEnum unspecified, Func<TEnum, string> toStored)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>()
            .Where(value => !EqualityComparer<TEnum>.Default.Equals(value, unspecified))
            .Select(toStored)
            .ToArray();

        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }
}
