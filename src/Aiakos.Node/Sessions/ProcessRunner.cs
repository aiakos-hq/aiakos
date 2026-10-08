using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace Aiakos.Node.Sessions;

public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment, ReadOnlyMemory<byte>? Stdin, TimeSpan Timeout,
    bool RetainStdoutTail = false);

public enum ProcessOutcome { Exited, TimedOut, StartFailed }

public sealed record ProcessResult(ProcessOutcome Outcome, int? ExitCode, byte[] Stdout, byte[] Stderr,
    bool StdoutTruncated, bool StderrTruncated);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
}

public sealed class ProcessRunner : IProcessRunner, IDisposable
{
    private const int PipeChunkBytes = 65536;
    private const int MaxOutputBytes = 1048576;
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMilliseconds(4294967294d);
    private static readonly byte[] NoBytes = [];

    private readonly SemaphoreSlim _concurrency;
    private readonly TimeProvider _timeProvider;
    private readonly object _lifetimeSync = new();
    private int _operations;
    private bool _disposed;
    private bool _semaphoreDisposed;

    public ProcessRunner(int maxConcurrency = 8, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Timeout <= TimeSpan.Zero || request.Timeout > MaximumTimeout)
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout must be greater than zero and no more than 4294967294 milliseconds.");

        lock (_lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _operations++;
        }

        var acquired = false;
        try
        {
            await _concurrency.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            ct.ThrowIfCancellationRequested();

            using var process = new Process { StartInfo = CreateStartInfo(request) };
            try
            {
                if (!process.Start()) return StartFailed();
            }
            catch (Exception exception) when (IsStartFailure(exception))
            {
                return StartFailed();
            }

            using var timeout = new CancellationTokenSource(request.Timeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var stdinTask = WriteStdinAsync(process.StandardInput.BaseStream, request.Stdin, linked.Token);
            var stdoutTask = DrainAsync(process.StandardOutput.BaseStream, linked.Token, request.RetainStdoutTail);
            var stderrTask = DrainAsync(process.StandardError.BaseStream, linked.Token);
            var exitTask = process.WaitForExitAsync(linked.Token);

            try
            {
                await Task.WhenAll(stdinTask, stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested)
                {
                    await StopAndJoinAsync(process, stdinTask, stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    return TimedOut(stdoutTask, stderrTask);
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                await StopAndJoinAsync(process, stdinTask, stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return TimedOut(stdoutTask, stderrTask);
            }
            catch
            {
                await StopAndJoinAsync(process, stdinTask, stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                throw;
            }

            return new ProcessResult(ProcessOutcome.Exited, process.ExitCode,
                stdoutTask.Result.Bytes, stderrTask.Result.Bytes,
                stdoutTask.Result.Truncated, stderrTask.Result.Truncated);
        }
        finally
        {
            if (acquired) _concurrency.Release();
            lock (_lifetimeSync)
            {
                _operations--;
                DisposeSemaphoreWhenIdle();
            }
        }
    }

    public void Dispose()
    {
        lock (_lifetimeSync)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeSemaphoreWhenIdle();
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment.Clear();
        foreach (var (name, value) in request.Environment) startInfo.Environment.Add(name, value);
        return startInfo;
    }

    private static async Task WriteStdinAsync(Stream stream, ReadOnlyMemory<byte>? input, CancellationToken ct)
    {
        try
        {
            if (input is { } bytes)
            {
                while (!bytes.IsEmpty)
                {
                    var length = Math.Min(PipeChunkBytes, bytes.Length);
                    try
                    {
                        await stream.WriteAsync(bytes[..length], ct).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        return;
                    }
                    bytes = bytes[length..];
                }
                try
                {
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    return;
                }
            }
        }
        catch (IOException)
        {
            // A child that closes stdin early has no use for the remaining input.
        }
        finally
        {
            try
            {
                stream.Close();
            }
            catch (IOException)
            {
                // Closing a pipe already closed by the child is also an early close.
            }
        }
    }

    private static async Task<DrainResult> DrainAsync(Stream stream, CancellationToken ct, bool retainTail = false)
    {
        using var retained = retainTail ? null : new MemoryStream(MaxOutputBytes);
        var tail = retainTail ? new byte[MaxOutputBytes] : Array.Empty<byte>();
        var tailStart = 0;
        var tailLength = 0;
        var chunk = new byte[PipeChunkBytes];
        var truncated = false;
        long bytesRead = 0;
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (ct.IsCancellationRequested &&
                                               exception is IOException or ObjectDisposedException)
            {
                break;
            }

            if (read == 0) break;
            if (!retainTail)
            {
                var keep = Math.Min(read, MaxOutputBytes - (int)retained!.Length);
                if (keep > 0) retained.Write(chunk, 0, keep);
                if (keep < read) truncated = true;
                continue;
            }

            bytesRead += read;
            if (read >= MaxOutputBytes)
            {
                chunk.AsSpan(read - MaxOutputBytes, MaxOutputBytes).CopyTo(tail);
                tailStart = 0;
                tailLength = MaxOutputBytes;
                if (bytesRead > MaxOutputBytes) truncated = true;
                continue;
            }

            var overflow = Math.Max(0, tailLength + read - MaxOutputBytes);
            tailStart = (tailStart + overflow) % MaxOutputBytes;
            tailLength -= overflow;
            var tailEnd = (tailStart + tailLength) % MaxOutputBytes;
            var firstPart = Math.Min(read, MaxOutputBytes - tailEnd);
            chunk.AsSpan(0, firstPart).CopyTo(tail.AsSpan(tailEnd));
            if (firstPart < read)
                chunk.AsSpan(firstPart, read - firstPart).CopyTo(tail);
            tailLength += read;
            if (bytesRead > MaxOutputBytes) truncated = true;
        }

        if (!retainTail)
            return new DrainResult(retained!.ToArray(), truncated);

        var output = new byte[tailLength];
        var tailFirstPart = Math.Min(tailLength, MaxOutputBytes - tailStart);
        tail.AsSpan(tailStart, tailFirstPart).CopyTo(output);
        if (tailFirstPart < tailLength)
            tail.AsSpan(0, tailLength - tailFirstPart).CopyTo(output.AsSpan(tailFirstPart));
        return new DrainResult(output, truncated);
    }

    private static async Task StopAndJoinAsync(Process process, params Task[] operations)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException) when (HasExited(process))
        {
        }

        ClosePipe(process.StandardInput.BaseStream);
        ClosePipe(process.StandardOutput.BaseStream);
        ClosePipe(process.StandardError.BaseStream);
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (HasExited(process))
        {
        }

        foreach (var operation in operations)
            await IgnoreTeardownFailureAsync(operation).ConfigureAwait(false);
    }

    private static async Task IgnoreTeardownFailureAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void ClosePipe(Stream pipe)
    {
        try
        {
            pipe.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static DrainResult GetCompleted(Task<DrainResult> task) =>
        task.IsCompletedSuccessfully ? task.Result : new DrainResult(NoBytes, false);

    private static bool IsStartFailure(Exception exception) => exception is
        Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or InvalidOperationException or NotSupportedException;

    private static ProcessResult StartFailed() =>
        new(ProcessOutcome.StartFailed, null, NoBytes, NoBytes, false, false);

    private static ProcessResult TimedOut(Task<DrainResult> stdout, Task<DrainResult> stderr)
    {
        var output = GetCompleted(stdout);
        var error = GetCompleted(stderr);
        return new ProcessResult(ProcessOutcome.TimedOut, null, output.Bytes, error.Bytes,
            output.Truncated, error.Truncated);
    }

    private void DisposeSemaphoreWhenIdle()
    {
        if (_disposed && _operations == 0 && !_semaphoreDisposed)
        {
            _semaphoreDisposed = true;
            _concurrency.Dispose();
        }
    }

    private sealed record DrainResult(byte[] Bytes, bool Truncated);
}
