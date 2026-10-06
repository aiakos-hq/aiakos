namespace Aiakos.Node.Sessions;

public sealed class LaunchFiles : ILaunchFiles
{
    private const UnixFileMode ExecutableBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                                                  UnixFileMode.OtherExecute;

    public bool ExecutableExists(string path)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(path))
            return false;

        try
        {
            return (File.GetUnixFileMode(path) & ExecutableBits) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);
}
