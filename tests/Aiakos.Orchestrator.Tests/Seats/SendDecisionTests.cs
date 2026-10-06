using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SendDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void T5SendDecisionAppliesRejectionsInOrderAndAllowsOnlyEligibleSends()
    {
        var disconnected = Apply(State(SessionValue.Present, ActivityValue.Idle),
            new SendRequested(false, false, true));
        Assert.Equal(new Rejected("NODE_NOT_CONNECTED"), disconnected.Reply);
        Assert.Contains(disconnected.Findings,
            finding => finding.Kind == SeatVocabulary.FindingNodeNotConnected && finding.Open);
        Assert.Equal(SessionValue.Present, disconnected.State.KnownSession);
        SeatAssert.Invariants(disconnected);

        AssertRejected(SessionValue.Unknown, ActivityValue.Unknown, false, true, "SEAT_STATE_UNKNOWN");
        AssertRejected(SessionValue.Absent, ActivityValue.None, false, false, "SEAT_NOT_PRESENT");
        AssertRejected(SessionValue.Starting, ActivityValue.Unknown, false, false, "SEAT_NOT_PRESENT");
        AssertRejected(SessionValue.Exited, ActivityValue.None, false, true, "SEAT_NOT_PRESENT");
        AssertRejected(SessionValue.Present, ActivityValue.Idle, false, true, "DELIVERY_IN_FLIGHT");
        AssertRejected(SessionValue.Present, ActivityValue.NeedsInput, false, true, "DELIVERY_IN_FLIGHT");
        AssertRejected(SessionValue.Present, ActivityValue.Working, false, false, "SEAT_WORKING");
        AssertRejected(SessionValue.Present, ActivityValue.NeedsInput, false, false, "SEAT_NEEDS_INPUT");
        AssertRejected(SessionValue.Present, ActivityValue.NeedsInput, true, false, "SEAT_NEEDS_INPUT");
        AssertRejected(SessionValue.Present, ActivityValue.Unknown, false, false, "SEAT_ACTIVITY_UNKNOWN");

        var unknown = State(SessionValue.Present, ActivityValue.Unknown);
        var forced = Apply(unknown,
            new SendRequested(true, true, false));
        Assert.Equal(new Accepted(Forced: true), forced.Reply);
        Assert.Equal(unknown, forced.State);
        Assert.Empty(forced.Findings);
        SeatAssert.Invariants(forced);

        var idleState = State(SessionValue.Present, ActivityValue.Idle);
        var idle = Apply(idleState,
            new SendRequested(false, true, false));
        Assert.Equal(new Accepted(), idle.Reply);
        Assert.Equal(idleState, idle.State);
        Assert.Empty(idle.Findings);
        SeatAssert.Invariants(idle);
    }

    private static void AssertRejected(SessionValue session, ActivityValue activity, bool force,
        bool deliveryInFlight, string reason)
    {
        var state = State(session, activity);
        var step = Apply(state, new SendRequested(force, true, deliveryInFlight));

        Assert.Equal(new Rejected(reason), step.Reply);
        Assert.Equal(state, step.State);
        Assert.Empty(step.Effects);
        SeatAssert.Invariants(step);
    }

    private static SeatStep Apply(SeatState state, SendRequested input) =>
        SeatStateMachine.Apply(state, input, new ClaudeLike(), Now);

    private static SeatState State(SessionValue session, ActivityValue activity)
    {
        var sessionReason = session == SessionValue.Unknown ? SeatVocabulary.SessionReasonSourcesDisagree : null;
        var activityReason = activity switch
        {
            ActivityValue.Unknown when session == SessionValue.Starting => SeatVocabulary.ActivityReasonNotReady,
            ActivityValue.Unknown when session == SessionValue.Unknown => SeatVocabulary.ActivityReasonSessionUnknown,
            ActivityValue.Unknown => SeatVocabulary.ActivityReasonQuietTimeout,
            _ => null
        };
        return SeatState.Initial(Now) with
        {
            Session = session,
            KnownSession = session,
            SessionReason = sessionReason,
            KnownSessionReason = sessionReason,
            Activity = activity,
            KnownActivity = activity,
            ActivityReason = activityReason,
            KnownActivityReason = activityReason,
            Resumability = ResumabilityValue.Resumable
        };
    }
}
