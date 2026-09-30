namespace Aiakos.Node;

/// <summary>
/// Exponential reconnect backoff with jitter (spec 0001 R35): starts at 1 s, doubles, capped at
/// 30 s, ±20 % jitter, reset after a successful connect. The jittered delay never exceeds the
/// cap, so a restarted orchestrator is reached within about 30 s (AC10).
/// Not thread-safe; one instance per connection loop.
/// </summary>
public sealed class Backoff
{
    public static readonly TimeSpan DefaultInitial = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultMaximum = TimeSpan.FromSeconds(30);
    public const double DefaultJitter = 0.2;

    private readonly TimeSpan _initial;
    private readonly TimeSpan _maximum;
    private readonly double _jitter;
    private readonly Func<double> _random;
    private TimeSpan _next;

    /// <param name="initial">First delay.</param>
    /// <param name="maximum">Cap for the base delay and the jittered delay.</param>
    /// <param name="jitter">Relative jitter, e.g. 0.2 for ±20 %.</param>
    /// <param name="random">Uniform source in [0, 1); injectable for deterministic tests.</param>
    public Backoff(TimeSpan initial, TimeSpan maximum, double jitter, Func<double>? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initial, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, initial);
        ArgumentOutOfRangeException.ThrowIfNegative(jitter);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(jitter, 1.0);

        _initial = initial;
        _maximum = maximum;
        _jitter = jitter;
        _random = random ?? Random.Shared.NextDouble;
        _next = initial;
    }

    /// <summary>The spec defaults: 1 s, doubling, cap 30 s, ±20 %.</summary>
    public static Backoff CreateDefault() => new(DefaultInitial, DefaultMaximum, DefaultJitter);

    /// <summary>Returns the next delay and advances the sequence.</summary>
    public TimeSpan NextDelay()
    {
        var baseDelay = _next;
        _next = baseDelay >= _maximum / 2 ? _maximum : baseDelay * 2;

        var factor = 1.0 + (_jitter * ((2.0 * _random()) - 1.0));
        var delay = baseDelay * factor;
        return delay > _maximum ? _maximum : delay;
    }

    /// <summary>Starts the sequence again from the initial delay (after a successful connect).</summary>
    public void Reset() => _next = _initial;
}
