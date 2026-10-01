namespace Aiakos.Hosting.Wsl;

/// <summary>
/// Runs a short-lived process and captures its output. The preflight uses it so tests can replace
/// <c>wsl.exe</c> with a fake.
/// </summary>
public interface IWslProcessRunner
{
    /// <summary>Runs <paramref name="fileName"/> with <paramref name="arguments"/> to completion.</summary>
    /// <exception cref="WslProcessStartException">The process could not be started at all.</exception>
    Task<WslProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>The outcome of a process run by <see cref="IWslProcessRunner"/>.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">Decoded standard output.</param>
/// <param name="StandardError">Decoded standard error.</param>
public sealed record WslProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>The process could not be started (for example, <c>wsl.exe</c> is not installed).</summary>
public sealed class WslProcessStartException : Exception
{
    public WslProcessStartException()
    {
    }

    public WslProcessStartException(string message)
        : base(message)
    {
    }

    public WslProcessStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
