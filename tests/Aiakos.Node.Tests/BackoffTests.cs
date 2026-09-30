namespace Aiakos.Node.Tests;

public sealed class BackoffTests
{
    private static readonly double[] ExpectedSeconds = [1, 2, 4, 8, 16, 30, 30];

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.999999)]
    public void SequenceDoublesFromOneSecondToTheCapWithinJitter(double random)
    {
        var backoff = new Backoff(Backoff.DefaultInitial, Backoff.DefaultMaximum, Backoff.DefaultJitter, () => random);

        foreach (var expected in ExpectedSeconds)
        {
            var actual = backoff.NextDelay().TotalSeconds;
            Assert.InRange(actual, expected * 0.8, Math.Min(expected * 1.2, 30.0));
        }
    }

    [Fact]
    public void WithoutJitterTheSequenceIsExact()
    {
        var backoff = new Backoff(Backoff.DefaultInitial, Backoff.DefaultMaximum, 0.0);

        var actual = Enumerable.Range(0, ExpectedSeconds.Length).Select(_ => backoff.NextDelay().TotalSeconds).ToArray();

        Assert.Equal(ExpectedSeconds, actual);
    }

    [Fact]
    public void JitterSpansPlusAndMinusTwentyPercentBelowTheCap()
    {
        Assert.Equal(0.8, new Backoff(Backoff.DefaultInitial, Backoff.DefaultMaximum, Backoff.DefaultJitter, () => 0.0).NextDelay().TotalSeconds, 6);
        Assert.Equal(1.2, new Backoff(Backoff.DefaultInitial, Backoff.DefaultMaximum, Backoff.DefaultJitter, () => 1.0).NextDelay().TotalSeconds, 6);
    }

    [Fact]
    public void DelayNeverExceedsTheCap()
    {
        var backoff = new Backoff(Backoff.DefaultInitial, Backoff.DefaultMaximum, Backoff.DefaultJitter, () => 1.0);

        for (var i = 0; i < 20; i++)
        {
            Assert.True(backoff.NextDelay() <= Backoff.DefaultMaximum);
        }
    }

    [Fact]
    public void ResetStartsAgainFromOneSecond()
    {
        var backoff = new Backoff(Backoff.DefaultInitial, Backoff.DefaultMaximum, 0.0);
        for (var i = 0; i < 6; i++)
        {
            backoff.NextDelay();
        }

        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(2), backoff.NextDelay());
    }

    [Fact]
    public void DefaultUsesTheSpecValues()
    {
        var backoff = Backoff.CreateDefault();

        Assert.InRange(backoff.NextDelay().TotalSeconds, 0.8, 1.2);
        Assert.InRange(backoff.NextDelay().TotalSeconds, 1.6, 2.4);
    }
}
