using Grpc.Core;

namespace Aiakos.Node.Link;

public sealed class NodeReconnectDelay
{
    private const long InitialCeilingTicks = TimeSpan.TicksPerSecond / 2;
    private const long MaxCeilingTicks = 300 * TimeSpan.TicksPerSecond;
    private const long NormalCapTicks = 30 * TimeSpan.TicksPerSecond;
    private readonly Func<double> _random;
    private long _ceilingTicks = InitialCeilingTicks;

    public NodeReconnectDelay(Func<double>? random = null)
    {
        _random = random ?? Random.Shared.NextDouble;
    }

    public TimeSpan NextDelay(StatusCode status)
    {
        var statusCap = status is StatusCode.Unauthenticated or StatusCode.PermissionDenied or
            StatusCode.FailedPrecondition
            ? MaxCeilingTicks
            : NormalCapTicks;
        var drawCeiling = Math.Min(_ceilingTicks, statusCap);
        var delayTicks = (long)Math.Floor(_random() * drawCeiling);

        _ceilingTicks = _ceilingTicks >= MaxCeilingTicks / 2
            ? MaxCeilingTicks
            : _ceilingTicks * 2;

        return TimeSpan.FromTicks(delayTicks);
    }

    public void Reset() => _ceilingTicks = InitialCeilingTicks;
}
