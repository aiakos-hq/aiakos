using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

public static class DeliveryStateMachine
{
    private static readonly IReadOnlyList<FindingChange> NoFindings = Array.Empty<FindingChange>();

    public static DeliveryStep Apply(DeliveryState state, DeliveryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (IsFinal(state))
        {
            return new DeliveryStep(state, NoFindings);
        }

        return state switch
        {
            DeliveryState.Pending => ApplyPending(input),
            DeliveryState.Sent => ApplySent(input),
            _ => new DeliveryStep(state, NoFindings)
        };
    }

    private static DeliveryStep ApplyPending(DeliveryInput input) => input switch
    {
        DeliveryDispatched => new DeliveryStep(DeliveryState.Sent, NoFindings),
        DeliveryDispatchFailed => new DeliveryStep(DeliveryState.NotDelivered, NoFindings),
        _ => new DeliveryStep(DeliveryState.Pending, NoFindings)
    };

    private static DeliveryStep ApplySent(DeliveryInput input) => input switch
    {
        DeliveryResultArrived result => ApplyResult(result.Outcome),
        DeliveryNotCompleted notCompleted => ApplyNotCompleted(notCompleted.Status),
        DeliveryNodeReplaced => new DeliveryStep(DeliveryState.Unknown, NoFindings),
        _ => new DeliveryStep(DeliveryState.Sent, NoFindings)
    };

    private static DeliveryStep ApplyResult(DeliveryOutcome outcome) => outcome switch
    {
        DeliveryOutcome.Confirmed => new DeliveryStep(DeliveryState.Confirmed, NoFindings),
        DeliveryOutcome.SubmittedUnconfirmed => new DeliveryStep(DeliveryState.SubmittedUnconfirmed,
            [new FindingChange(SeatVocabulary.FindingDeliveryUnconfirmed, true)]),
        DeliveryOutcome.NotDelivered => new DeliveryStep(DeliveryState.NotDelivered, NoFindings),
        _ => new DeliveryStep(DeliveryState.Unknown, NoFindings)
    };

    private static DeliveryStep ApplyNotCompleted(CommandStatus status) => status switch
    {
        CommandStatus.Rejected or CommandStatus.Failed or CommandStatus.TimedOut =>
            new DeliveryStep(DeliveryState.Failed, NoFindings),
        CommandStatus.Unspecified => new DeliveryStep(DeliveryState.Unknown, NoFindings),
        CommandStatus.Completed => new DeliveryStep(DeliveryState.Sent, NoFindings),
        _ => new DeliveryStep(DeliveryState.Unknown, NoFindings)
    };

    private static bool IsFinal(DeliveryState state) => state is
        DeliveryState.Confirmed or
        DeliveryState.SubmittedUnconfirmed or
        DeliveryState.NotDelivered or
        DeliveryState.Failed or
        DeliveryState.Unknown;
}
