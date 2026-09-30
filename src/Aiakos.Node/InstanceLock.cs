using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace Aiakos.Node;

/// <summary>
/// Single-instance lock on <c>$AIAKOS_HOME/node.lock</c> (spec 0001 R38). The lock is held for
/// the life of the process and released by the kernel when the process dies, so a held lock means
/// a live holder; the node never kills it (rule 3).
/// </summary>
/// <remarks>
/// On Linux, <see cref="FileShare.None"/> makes .NET take <c>flock(LOCK_EX | LOCK_NB)</c>. A flock
/// belongs to the open file description, so two processes and two handles in one process
/// exclude each other. Every <see cref="FileStream"/> open takes at least <c>LOCK_SH</c>, so the
/// refusal reads the holder's pid through a raw <c>open(2)</c>, which flock (advisory) does not
/// block. On Windows the holder shares read access only, so a second locker (which asks for write
/// access) is refused while the refusal can still read the pid.
/// </remarks>
public sealed partial class InstanceLock : IDisposable
{
    /// <summary>Name of the lock file in the node home.</summary>
    public const string FileName = "node.lock";

    private readonly FileStream _stream;

    private InstanceLock(string filePath, FileStream stream)
    {
        FilePath = filePath;
        _stream = stream;
    }

    /// <summary>Full path of the lock file.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Creates <paramref name="home"/> if missing and takes the lock. On success, writes the pid
    /// and the start time into the file. When another holder has it, returns <see langword="false"/>
    /// with the holder's pid, or <see langword="null"/> when the file does not name one (yet).
    /// Other I/O failures (permissions, bad path) throw.
    /// </summary>
    public static bool TryAcquire(
        string home,
        [NotNullWhen(true)] out InstanceLock? instanceLock,
        out int? holderPid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        Directory.CreateDirectory(home);
        var path = Path.Combine(home, FileName);

        FileStream stream;
        try
        {
            stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None,
                bufferSize: 0);
        }
        catch (IOException ex) when (ex.GetType() == typeof(IOException))
        {
            // Exactly IOException: sharing violation (Windows) or EWOULDBLOCK from flock (Unix).
            // Subclasses (file/directory not found, path too long) are real errors.
            instanceLock = null;
            holderPid = ReadHolderPid(path);
            return false;
        }

        try
        {
            var content = string.Create(
                CultureInfo.InvariantCulture,
                $"{Environment.ProcessId}\n{DateTimeOffset.UtcNow:O}\n");
            stream.SetLength(0);
            stream.Write(Encoding.UTF8.GetBytes(content));
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        instanceLock = new InstanceLock(path, stream);
        holderPid = null;
        return true;
    }

    /// <summary>Reads the pid from a lock file without taking any lock; <see langword="null"/> when unreadable.</summary>
    public static int? ReadHolderPid(string path)
    {
        string content;
        try
        {
            content = OperatingSystem.IsWindows() ? ReadShared(path) : ReadRaw(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var firstLine = content.Split('\n', 2)[0].Trim();
        return int.TryParse(firstLine, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
    }

    /// <summary>Releases the lock. The file stays; the next holder overwrites it.</summary>
    public void Dispose() => _stream.Dispose();

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ReadRaw(string path)
    {
        const int ReadOnly = 0; // O_RDONLY
        var fd = Open(path, ReadOnly);
        if (fd < 0)
        {
            throw new IOException($"open({path}) failed with errno {Marshal.GetLastPInvokeError()}.");
        }

        using var handle = new SafeFileHandle(fd, ownsHandle: true);
        var buffer = new byte[256];
        var read = RandomAccess.Read(handle, buffer, fileOffset: 0);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);
}
