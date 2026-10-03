using Aiakos.Node.Sessions;

namespace Aiakos.Node.Testing;

public sealed class FakeLaunchFiles : ILaunchFiles
{
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);

    public bool ExecutableExists(string path) => !_removed.Contains(path);

    public bool DirectoryExists(string path) => !_removed.Contains(path);

    public void Remove(string path) => _removed.Add(path);
}
