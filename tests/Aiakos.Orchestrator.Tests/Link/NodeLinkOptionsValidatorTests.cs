using Aiakos.Orchestrator.Link;

using Microsoft.Extensions.Options;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeLinkOptionsValidatorTests
{
    [Theory]
    [InlineData(-10000)]
    [InlineData(0)]
    [InlineData(5000)]
    [InlineData(86400000000000)]
    [InlineData(42949672940001)]
    public void RejectsHelloTimeoutOutsideTheInclusiveRange(long ticks)
    {
        var options = new NodeLinkOptions { HelloTimeout = TimeSpan.FromTicks(ticks) };

        var result = new NodeLinkOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Equal(
            ["NodeLink HelloTimeout must be between 1 and 4294967294 milliseconds."],
            result.Failures);
    }

    [Theory]
    [InlineData("heartbeat", -10000, "NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.")]
    [InlineData("heartbeat", 0, "NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.")]
    [InlineData("heartbeat", 5000, "NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.")]
    [InlineData("heartbeat", 86400000000000, "NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.")]
    [InlineData("heartbeat", 42949672940001, "NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.")]
    [InlineData("liveness", -10000, "NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.")]
    [InlineData("liveness", 0, "NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.")]
    [InlineData("liveness", 5000, "NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.")]
    [InlineData("liveness", 86400000000000, "NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.")]
    [InlineData("liveness", 42949672940001, "NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.")]
    public void RejectsHeartbeatAndLivenessOutsideTheirInclusiveRanges(string field, long ticks, string failure)
    {
        var options = new NodeLinkOptions();
        if (field == "heartbeat")
            options.HeartbeatInterval = TimeSpan.FromTicks(ticks);
        else
            options.LivenessTimeout = TimeSpan.FromTicks(ticks);

        var result = new NodeLinkOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Equal([failure], result.Failures);
    }

    [Theory]
    [InlineData(50000, 50000)]
    [InlineData(60000, 50000)]
    public void RejectsHeartbeatNotLessThanLivenessWithoutOtherFailures(long heartbeatTicks, long livenessTicks)
    {
        var options = new NodeLinkOptions
        {
            HeartbeatInterval = TimeSpan.FromTicks(heartbeatTicks),
            LivenessTimeout = TimeSpan.FromTicks(livenessTicks),
        };

        var result = new NodeLinkOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Equal(["NodeLink HeartbeatInterval must be less than LivenessTimeout."], result.Failures);
    }

    [Fact]
    public void CollectsRangeFailuresInContractOrderAndSkipsTheInvalidRelation()
    {
        var options = new NodeLinkOptions
        {
            HelloTimeout = TimeSpan.Zero,
            HeartbeatInterval = TimeSpan.Zero,
            LivenessTimeout = TimeSpan.Zero,
        };

        var result = new NodeLinkOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Equal(
        [
            "NodeLink HelloTimeout must be between 1 and 4294967294 milliseconds.",
            "NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.",
            "NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.",
        ], result.Failures);
    }

    [Fact]
    public void AcceptsInclusiveTimeoutBoundariesAndDefaultOptions()
    {
        var maximum = TimeSpan.FromMilliseconds(4294967294L);
        var validator = new NodeLinkOptionsValidator();

        Assert.True(validator.Validate(Options.DefaultName, new NodeLinkOptions()).Succeeded);
        Assert.True(validator.Validate(Options.DefaultName, new NodeLinkOptions
        {
            HelloTimeout = TimeSpan.FromMilliseconds(1),
            HeartbeatInterval = TimeSpan.FromMilliseconds(1),
            LivenessTimeout = TimeSpan.FromMilliseconds(2),
        }).Succeeded);
        Assert.True(validator.Validate(Options.DefaultName, new NodeLinkOptions
        {
            HelloTimeout = maximum,
            HeartbeatInterval = maximum - TimeSpan.FromTicks(1),
            LivenessTimeout = maximum,
        }).Succeeded);
    }
}
