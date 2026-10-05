using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security;

namespace Aiakos.Spec;

internal enum SharedEntryKind
{
    RegularFile,
    Directory,
    Other
}

internal static class SharedFileClassifier
{
    private const int AtCurrentWorkingDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxType = 1;
    private const int StatxBufferSize = 256;
    private const int StatxModeOffset = 28;

    internal static bool TryClassify(string path, out SharedEntryKind kind)
    {
        kind = SharedEntryKind.Other;
        if (OperatingSystem.IsLinux())
            return TryClassifyLinux(path, out kind);
        if (OperatingSystem.IsWindows())
            return TryClassifyWindows(path, out kind);
        return false;
    }

    private static bool TryClassifyLinux(string path, out SharedEntryKind kind)
    {
        kind = SharedEntryKind.Other;
        var utf8Path = IntPtr.Zero;
        try
        {
            utf8Path = Marshal.StringToCoTaskMemUTF8(path);
            var stat = new byte[StatxBufferSize];
            if (Statx(AtCurrentWorkingDirectory, utf8Path, AtSymlinkNoFollow, StatxType, stat) != 0)
                return false;

            var mask = BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(0, sizeof(uint)));
            if ((mask & StatxType) == 0)
                return false;

            var mode = BinaryPrimitives.ReadUInt16LittleEndian(stat.AsSpan(StatxModeOffset, sizeof(ushort)));
            kind = (mode & 0xF000) switch
            {
                0x8000 => SharedEntryKind.RegularFile,
                0x4000 => SharedEntryKind.Directory,
                _ => SharedEntryKind.Other
            };
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (MarshalDirectiveException)
        {
            return false;
        }
        finally
        {
            if (utf8Path != IntPtr.Zero)
                Marshal.FreeCoTaskMem(utf8Path);
        }
    }

    private static bool TryClassifyWindows(string path, out SharedEntryKind kind)
    {
        kind = SharedEntryKind.Other;
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
                return true;
            kind = (attributes & FileAttributes.Directory) != 0
                ? SharedEntryKind.Directory
                : SharedEntryKind.RegularFile;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directoryFileDescriptor, IntPtr path, int flags, uint mask,
        [Out] byte[] buffer);
}
