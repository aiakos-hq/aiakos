using Aspire.Hosting.ApplicationModel;

namespace Aiakos.Hosting.Wsl;

/// <summary>
/// A Linux process started inside a WSL distro through <c>wsl.exe</c> (spec 0001 R22–R23).
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="distro">The WSL distro (<c>wsl.exe -d</c>).</param>
/// <param name="linuxPath">The executable inside the distro: absolute, or relative to the WSL user's home.</param>
/// <param name="workingDirectory">The Windows working directory of <c>wsl.exe</c>.</param>
public sealed class WslExecutableResource(string name, string distro, string linuxPath, string workingDirectory)
    : ExecutableResource(name, WslExecutableResource.WslCommand, workingDirectory)
{
    /// <summary>The Windows launcher every WSL resource runs.</summary>
    public const string WslCommand = "wsl.exe";

    /// <summary>The WSL distro the process runs in.</summary>
    public string Distro { get; } = distro;

    /// <summary>The executable inside the distro. <c>~</c> is not expanded (no shell sits in between).</summary>
    public string LinuxPath { get; } = linuxPath;
}
