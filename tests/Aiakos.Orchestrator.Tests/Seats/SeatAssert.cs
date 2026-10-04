using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

internal static class SeatAssert
{
    public static void Invariants(SeatStep step)
    {
        AssertAxis(step.State.KnownSession, step.State.KnownSessionReason,
            step.State.KnownActivity, step.State.KnownActivityReason);
        AssertAxis(step.State.Session, step.State.SessionReason,
            step.State.Activity, step.State.ActivityReason);
        if (step.State.Overlay is null)
        {
            Assert.Equal(step.State.KnownSession, step.State.Session);
            Assert.Equal(step.State.KnownSessionReason, step.State.SessionReason);
            Assert.Equal(step.State.KnownActivity, step.State.Activity);
            Assert.Equal(step.State.KnownActivityReason, step.State.ActivityReason);
        }
    }

    private static void AssertAxis(SessionValue session, string? sessionReason,
        ActivityValue activity, string? activityReason)
    {
        Assert.Equal(session is SessionValue.Absent or SessionValue.Exited,
            activity == ActivityValue.None);
        if (session == SessionValue.Unknown)
            Assert.NotNull(sessionReason);
        if (activity == ActivityValue.Unknown)
            Assert.NotNull(activityReason);
    }
}
