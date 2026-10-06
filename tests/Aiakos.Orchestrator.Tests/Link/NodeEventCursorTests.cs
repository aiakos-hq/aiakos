using Aiakos.Orchestrator.Link;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeEventCursorTests
{
    private static readonly Guid EpochA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EpochB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void EvaluatesAbsentEpochDuplicateSkipAndChangedEpoch()
    {
        Assert.Equal(new NodeEventCursorResult(false, false, EpochA, 2),
            NodeEventCursor.Evaluate(null, 1, EpochA, 1));
        Assert.Equal(new NodeEventCursorResult(false, true, EpochA, 5),
            NodeEventCursor.Evaluate(null, 1, EpochA, 4));
        Assert.Equal(new NodeEventCursorResult(true, false, EpochA, 6),
            NodeEventCursor.Evaluate(EpochA, 6, EpochA, 5));
        Assert.Equal(new NodeEventCursorResult(false, false, EpochA, 7),
            NodeEventCursor.Evaluate(EpochA, 6, EpochA, 6));
        Assert.Equal(new NodeEventCursorResult(false, true, EpochA, 9),
            NodeEventCursor.Evaluate(EpochA, 6, EpochA, 8));
        Assert.Equal(new NodeEventCursorResult(false, true, EpochB, 2),
            NodeEventCursor.Evaluate(EpochA, 42, EpochB, 1));
        Assert.Equal(new NodeEventCursorResult(false, true, EpochA, (ulong)long.MaxValue),
            NodeEventCursor.Evaluate(null, 1, EpochA, (ulong)long.MaxValue - 1));
        Assert.Equal(new NodeEventCursorResult(true, false, EpochA, (ulong)long.MaxValue),
            NodeEventCursor.Evaluate(EpochA, (ulong)long.MaxValue, EpochA, (ulong)long.MaxValue - 1));
    }

    [Fact]
    public void RejectsInvalidCursorBoundsAndEmptyEpochsWithOneFixedMessage()
    {
        var invalid = new (Guid? Committed, ulong NextSeq, Guid Incoming, ulong Seq)[]
        {
            (null, 1, Guid.Empty, 1),
            (Guid.Empty, 1, EpochA, 1),
            (null, 0, EpochA, 1),
            (null, (ulong)long.MaxValue + 1, EpochA, 1),
            (null, 1, EpochA, 0),
            (null, 1, EpochA, (ulong)long.MaxValue),
        };

        foreach (var (committed, nextSeq, incoming, seq) in invalid)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                NodeEventCursor.Evaluate(committed, nextSeq, incoming, seq));
            Assert.Equal("Invalid event cursor.", exception.Message);
            Assert.Null(exception.ParamName);
            Assert.Null(exception.InnerException);
        }
    }
}
