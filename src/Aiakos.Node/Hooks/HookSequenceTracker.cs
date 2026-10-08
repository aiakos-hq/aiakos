namespace Aiakos.Node.Hooks;

public sealed class HookSequenceTracker
{
    private static readonly TimeSpan GapDelay = TimeSpan.FromSeconds(2);
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

    public HookSequenceTracker(TimeProvider timeProvider) => _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public void Receive(HookLaunch launch, ulong sourceSeq)
    {
        ArgumentNullException.ThrowIfNull(launch);
        lock (_gate)
        {
            var state = _states.GetValueOrDefault(launch.LaunchId);
            if (state is null) _states[launch.LaunchId] = new State(sourceSeq, _timeProvider.GetUtcNow(), Launch: launch);
            else if (sourceSeq > state.LastSequence) _states[launch.LaunchId] = state with { LastSequence = sourceSeq, LastReceived = _timeProvider.GetUtcNow(), Launch = launch };
        }
    }

    public IReadOnlyList<HookLaunch> TakeExpiredGaps()
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            var expired = _states.Where(pair => !pair.Value.Reported && now - pair.Value.LastReceived >= GapDelay).ToArray();
            foreach (var pair in expired) _states[pair.Key] = pair.Value with { Reported = true };
            return expired.Select(pair => pair.Value.Launch!).ToArray();
        }
    }

    private sealed record State(ulong LastSequence, DateTimeOffset LastReceived, bool Reported = false, HookLaunch? Launch = null);
}
