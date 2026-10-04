using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class DeliveryTableTests
{
    [Fact]
    public void DispatchesPendingDeliveryToSent()
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Pending, new DeliveryDispatched());

        Assert.Equal(DeliveryState.Sent, step.State);
        Assert.Empty(step.Findings);
    }

    [Fact]
    public void MarksPendingDispatchFailureNotDelivered()
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Pending, new DeliveryDispatchFailed());

        Assert.Equal(DeliveryState.NotDelivered, step.State);
        Assert.Empty(step.Findings);
    }

    [Theory]
    [InlineData(DeliveryOutcome.Confirmed, DeliveryState.Confirmed)]
    [InlineData(DeliveryOutcome.NotDelivered, DeliveryState.NotDelivered)]
    public void AppliesRecognizedResultToSentDelivery(DeliveryOutcome outcome, DeliveryState expected)
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent, new DeliveryResultArrived(outcome));

        Assert.Equal(expected, step.State);
        Assert.Empty(step.Findings);
    }

    [Fact]
    public void OpensFindingForSubmittedUnconfirmedResult()
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent,
            new DeliveryResultArrived(DeliveryOutcome.SubmittedUnconfirmed));

        Assert.Equal(DeliveryState.SubmittedUnconfirmed, step.State);
        Assert.Equal([new FindingChange(SeatVocabulary.FindingDeliveryUnconfirmed, true)], step.Findings);
    }

    [Theory]
    [InlineData(DeliveryOutcome.Unspecified)]
    [InlineData((DeliveryOutcome)12345)]
    public void MakesSentDeliveryUnknownForUnrecognizedOutcome(DeliveryOutcome outcome)
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent, new DeliveryResultArrived(outcome));

        Assert.Equal(DeliveryState.Unknown, step.State);
        Assert.Empty(step.Findings);
    }

    [Theory]
    [InlineData(CommandStatus.Rejected)]
    [InlineData(CommandStatus.Failed)]
    [InlineData(CommandStatus.TimedOut)]
    public void MarksSentDeliveryFailedForUncompletedFailure(CommandStatus status)
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent, new DeliveryNotCompleted(status));

        Assert.Equal(DeliveryState.Failed, step.State);
        Assert.Empty(step.Findings);
    }

    [Theory]
    [InlineData(CommandStatus.Unspecified)]
    [InlineData((CommandStatus)12345)]
    public void MakesSentDeliveryUnknownForUnrecognizedCommandStatus(CommandStatus status)
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent, new DeliveryNotCompleted(status));

        Assert.Equal(DeliveryState.Unknown, step.State);
        Assert.Empty(step.Findings);
    }

    [Fact]
    public void IgnoresCompletedAsANotCompletedStatus()
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent,
            new DeliveryNotCompleted(CommandStatus.Completed));

        Assert.Equal(DeliveryState.Sent, step.State);
        Assert.Empty(step.Findings);
    }

    [Fact]
    public void MakesSentDeliveryUnknownWhenTheNodeIsReplaced()
    {
        var step = DeliveryStateMachine.Apply(DeliveryState.Sent, new DeliveryNodeReplaced());

        Assert.Equal(DeliveryState.Unknown, step.State);
        Assert.Empty(step.Findings);
    }

    [Fact]
    public void LeavesPendingDeliveryUnchangedForInputsWithoutPendingRows()
    {
        DeliveryInput[] inputs =
        [
            new DeliveryResultArrived(DeliveryOutcome.Confirmed),
            new DeliveryResultArrived(DeliveryOutcome.SubmittedUnconfirmed),
            new DeliveryResultArrived(DeliveryOutcome.NotDelivered),
            new DeliveryResultArrived(DeliveryOutcome.Unspecified),
            new DeliveryResultArrived((DeliveryOutcome)12345),
            new DeliveryNotCompleted(CommandStatus.Completed),
            new DeliveryNotCompleted(CommandStatus.Rejected),
            new DeliveryNotCompleted(CommandStatus.Failed),
            new DeliveryNotCompleted(CommandStatus.TimedOut),
            new DeliveryNotCompleted(CommandStatus.Unspecified),
            new DeliveryNotCompleted((CommandStatus)12345),
            new DeliveryNodeReplaced()
        ];

        foreach (var input in inputs)
        {
            var step = DeliveryStateMachine.Apply(DeliveryState.Pending, input);

            Assert.Equal(DeliveryState.Pending, step.State);
            Assert.Empty(step.Findings);
        }
    }

    [Fact]
    public void LeavesSentDeliveryUnchangedForInputsWithoutSentRows()
    {
        DeliveryInput[] inputs =
        [
            new DeliveryDispatched(),
            new DeliveryDispatchFailed(),
            new DeliveryNotCompleted(CommandStatus.Completed)
        ];

        foreach (var input in inputs)
        {
            var step = DeliveryStateMachine.Apply(DeliveryState.Sent, input);

            Assert.Equal(DeliveryState.Sent, step.State);
            Assert.Empty(step.Findings);
        }
    }

    [Fact]
    public void FinalStatesAbsorbEveryDeliveryInput()
    {
        DeliveryState[] finalStates =
        [
            DeliveryState.Confirmed,
            DeliveryState.SubmittedUnconfirmed,
            DeliveryState.NotDelivered,
            DeliveryState.Failed,
            DeliveryState.Unknown
        ];
        DeliveryInput[] inputs =
        [
            new DeliveryDispatched(),
            new DeliveryDispatchFailed(),
            new DeliveryResultArrived(DeliveryOutcome.Confirmed),
            new DeliveryResultArrived(DeliveryOutcome.SubmittedUnconfirmed),
            new DeliveryResultArrived(DeliveryOutcome.NotDelivered),
            new DeliveryResultArrived(DeliveryOutcome.Unspecified),
            new DeliveryResultArrived((DeliveryOutcome)12345),
            new DeliveryNotCompleted(CommandStatus.Completed),
            new DeliveryNotCompleted(CommandStatus.Rejected),
            new DeliveryNotCompleted(CommandStatus.Failed),
            new DeliveryNotCompleted(CommandStatus.TimedOut),
            new DeliveryNotCompleted(CommandStatus.Unspecified),
            new DeliveryNotCompleted((CommandStatus)12345),
            new DeliveryNodeReplaced()
        ];

        foreach (var state in finalStates)
        {
            foreach (var input in inputs)
            {
                var step = DeliveryStateMachine.Apply(state, input);

                Assert.Equal(state, step.State);
                Assert.Empty(step.Findings);
            }
        }
    }
}
