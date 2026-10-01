using System.ComponentModel;
using System.Diagnostics;

namespace Aiakos.Hosting.Wsl;

/// <summary>Runs processes with <see cref="Process"/>; the default <see cref="IWslProcessRunner"/>.</summary>
public sealed class WslProcessRunner : IWslProcessRunner
{
    /// <inheritdoc />
    public async Task<WslProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Ask wsl.exe for UTF-8; WslOutput still copes with UTF-16LE from older versions.
        startInfo.Environment["WSL_UTF8"] = "1";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new WslProcessStartException($"Could not start '{fileName}': {ex.Message}", ex);
        }

        process.StandardInput.Close();
        var stdout = ReadAllAsync(process.StandardOutput.BaseStream, cancellationToken);
        var stderr = ReadAllAsync(process.StandardError.BaseStream, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new WslProcessResult(
            process.ExitCode,
            WslOutput.Decode(await stdout.ConfigureAwait(false)),
            WslOutput.Decode(await stderr.ConfigureAwait(false)));
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
