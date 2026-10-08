namespace Aiakos.Node.Hooks;

public sealed class HookLaunchRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (HookLaunch Launch, DateTimeOffset Registered)> _tokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ended = new(StringComparer.Ordinal);

    private readonly TimeProvider _timeProvider;

    public HookLaunchRegistry(TimeProvider timeProvider) => _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public void Register(string token, HookLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(launch);
        lock (_gate)
        {
            _ended.Remove(launch.LaunchId);
            _tokens[token] = (launch, _timeProvider.GetUtcNow());
        }
    }

    public void End(string launchId)
    {
        ArgumentNullException.ThrowIfNull(launchId);
        lock (_gate)
        {
            _ended.Add(launchId);
            foreach (var token in _tokens.Where(pair => pair.Value.Launch.LaunchId == launchId).Select(pair => pair.Key).ToArray())
                _tokens.Remove(token);
        }
    }

    public bool TryResolve(string token, out HookLaunch? launch)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            if (_tokens.TryGetValue(token, out var entry) && !_ended.Contains(entry.Launch.LaunchId))
            {
                launch = entry.Launch;
                return true;
            }
            launch = null;
            return false;
        }
    }
}
