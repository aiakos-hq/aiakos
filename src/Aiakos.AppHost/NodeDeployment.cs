namespace Aiakos.AppHost;

/// <summary>How the node is published on Windows and installed into WSL (spec 0001 R19–R20).</summary>
public static class NodeDeployment
{
    /// <summary>The node project, relative to the repository root.</summary>
    public const string NodeProject = "src/Aiakos.Node";

    /// <summary>The publish output, relative to the repository root.</summary>
    public const string PublishDirectory = "artifacts/node/linux-x64";

    /// <summary>The node executable's file name.</summary>
    public const string NodeExecutable = "aiakos-node";

    /// <summary>
    /// Installs the published node into <c>$HOME/$2</c>. Run as <c>sh -c &lt;script&gt; sh
    /// &lt;windows publish dir&gt; &lt;home&gt;</c>: the values are positional arguments and are never
    /// interpolated into the script text. <c>wslpath -u</c> converts the path (never string
    /// manipulation), and the node always runs from the WSL filesystem, never from <c>/mnt/c</c>.
    /// </summary>
    public const string WslInstallScript = """
        set -eu
        src="$(wslpath -u "$1")"; home="$HOME/$2"
        mkdir -p "$home"; rm -rf "$home/node.staging"
        cp -r "$src" "$home/node.staging"; chmod +x "$home/node.staging/aiakos-node"
        rm -rf "$home/node"; mv "$home/node.staging" "$home/node"
        """;

    /// <summary>The <c>dotnet</c> arguments of <c>node-publish</c>, run from the repository root.</summary>
    public static string[] PublishArguments =>
        ["publish", NodeProject, "-c", "Debug", "-r", "linux-x64", "--self-contained", "-o", PublishDirectory];

    /// <summary>The installed node, relative to the WSL user's home.</summary>
    public static string InstalledNodePath(string home) => $"{home.TrimEnd('/')}/node/{NodeExecutable}";

    /// <summary>
    /// Finds the repository root by walking up from <paramref name="startDirectory"/> to the
    /// directory that contains <c>Aiakos.slnx</c>.
    /// </summary>
    public static string FindRepositoryRoot(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aiakos.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find the repository root (a directory containing Aiakos.slnx) above '{startDirectory}'. The dev AppHost runs from a clone of the repository.");
    }
}
