using System.Collections.Concurrent;

namespace Aiakos.Hosting.Wsl;

/// <summary>The outcome of a WSL preflight for one distro.</summary>
/// <param name="Succeeded">Whether the distro exists and uses mirrored networking.</param>
/// <param name="NetworkingMode">The detected mode, or <c>unknown</c> (rule 3: honest state).</param>
/// <param name="Message">An actionable message; on success, how the mode was determined.</param>
public sealed record WslPreflightResult(bool Succeeded, string NetworkingMode, string Message);

/// <summary>The preflight failed; the message says what to do.</summary>
public sealed class WslPreflightException : Exception
{
    public WslPreflightException()
    {
    }

    public WslPreflightException(string message)
        : base(message)
    {
    }

    public WslPreflightException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Checks, before a WSL resource starts, that its distro exists and runs in mirrored networking
/// mode (spec 0001 R26, RK1). NAT is not supported and there is no fallback.
/// </summary>
/// <remarks>
/// The check runs <c>wsl.exe -d &lt;distro&gt; --exec wslinfo --networking-mode</c>. If that fails
/// but the distro exists (<c>wslinfo</c> unavailable), it reads <c>networkingMode</c> from
/// <c>%USERPROFILE%\.wslconfig</c>, and reports <c>unknown</c> (and refuses) when neither works.
/// A successful result is cached per distro for the lifetime of this instance (one AppHost start);
/// a failure is not cached, so restarting the resource after fixing the cause checks again.
/// </remarks>
public sealed class WslPreflight(IWslProcessRunner runner, IWslConfigFile wslConfig)
{
    /// <summary>The only supported networking mode.</summary>
    public const string Mirrored = "mirrored";

    /// <summary>Reported when the mode cannot be determined.</summary>
    public const string Unknown = "unknown";

    /// <summary>The remedy included in every networking failure message.</summary>
    public const string Remedy =
        "Aiakos requires mirrored networking: add 'networkingMode=mirrored' under [wsl2] in "
        + @"%USERPROFILE%\.wslconfig, then run 'wsl --shutdown'. NAT is not supported.";

    private readonly ConcurrentDictionary<string, Lazy<Task<WslPreflightResult>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long one <c>wsl.exe</c> call may take (a cold distro start can take seconds).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Runs (or returns the cached successful) preflight for <paramref name="distro"/>.</summary>
    public async Task<WslPreflightResult> CheckAsync(string distro, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distro);

        var lazy = _cache.GetOrAdd(distro, d => new Lazy<Task<WslPreflightResult>>(() => RunAsync(d)));
        WslPreflightResult result;
        try
        {
            result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Evict(distro, lazy);
            throw;
        }

        if (!result.Succeeded)
        {
            Evict(distro, lazy);
        }

        return result;
    }

    /// <summary>Runs the preflight and throws <see cref="WslPreflightException"/> when it fails.</summary>
    public async Task<WslPreflightResult> EnsureAsync(string distro, CancellationToken cancellationToken)
    {
        var result = await CheckAsync(distro, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result : throw new WslPreflightException(result.Message);
    }

    private void Evict(string distro, Lazy<Task<WslPreflightResult>> lazy) =>
        _cache.TryRemove(new KeyValuePair<string, Lazy<Task<WslPreflightResult>>>(distro, lazy));

    private async Task<WslPreflightResult> RunAsync(string distro)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            return await RunCoreAsync(distro, timeout.Token).ConfigureAwait(false);
        }
        catch (WslProcessStartException ex)
        {
            return Fail(Unknown, $"WSL is not available ({ex.Message}). Install WSL2 and the distro '{distro}'.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Fail(Unknown, $"The networking mode of WSL distro '{distro}' is unknown: wsl.exe did not answer within {Timeout.TotalSeconds:0} s. {Remedy}");
        }
    }

    private async Task<WslPreflightResult> RunCoreAsync(string distro, CancellationToken cancellationToken)
    {
        var info = await runner.RunAsync(
            WslExecutableResource.WslCommand,
            ["-d", distro, "--exec", "wslinfo", "--networking-mode"],
            cancellationToken).ConfigureAwait(false);

        if (info.ExitCode == 0)
        {
            var mode = FirstLine(info.StandardOutput);
            return mode.Length == 0
                ? Fail(Unknown, $"The networking mode of WSL distro '{distro}' is unknown: 'wslinfo --networking-mode' printed nothing. {Remedy}")
                : Classify(distro, mode, "wslinfo --networking-mode");
        }

        // Non-zero exit: either the distro does not exist, or wslinfo is unavailable (RK1).
        if (!await DistroExistsAsync(distro, cancellationToken).ConfigureAwait(false))
        {
            return Fail(Unknown, $"WSL distro '{distro}' not found (wsl -l -v).");
        }

        var configured = WslConfigParser.GetNetworkingMode(wslConfig.ReadAllText());
        if (configured is null)
        {
            var detail = FirstLine(info.StandardError.Length > 0 ? info.StandardError : info.StandardOutput);
            var reason = detail.Length > 0 ? $"exit code {info.ExitCode}: {detail}" : $"exit code {info.ExitCode}";
            return Fail(Unknown, $"The networking mode of WSL distro '{distro}' is unknown: 'wslinfo --networking-mode' failed ({reason}) and %USERPROFILE%\\.wslconfig sets no networkingMode. {Remedy}");
        }

        return Classify(distro, configured, @"%USERPROFILE%\.wslconfig; wslinfo unavailable");
    }

    private static WslPreflightResult Classify(string distro, string mode, string source)
    {
        mode = mode.ToLowerInvariant();
        return mode == Mirrored
            ? new WslPreflightResult(true, Mirrored, $"WSL distro '{distro}' uses mirrored networking (from {source}).")
            : Fail(mode, $"WSL distro '{distro}' uses networking mode '{mode}' (from {source}). {Remedy}");
    }

    private async Task<bool> DistroExistsAsync(string distro, CancellationToken cancellationToken)
    {
        var list = await runner.RunAsync(WslExecutableResource.WslCommand, ["-l", "-q"], cancellationToken).ConfigureAwait(false);
        return list.ExitCode == 0
            && list.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(name => name.Equals(distro, StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

    private static WslPreflightResult Fail(string mode, string message) => new(false, mode, message);
}
