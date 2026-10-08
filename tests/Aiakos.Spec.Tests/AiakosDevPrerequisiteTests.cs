using System.Diagnostics;
using System.Runtime.Versioning;

namespace Aiakos.Spec.Tests;

public sealed class AiakosDevPrerequisiteTests
{
    private static readonly string[] Success =
    [
        "ok networking", "ok tools", "ok claude: account marker present; login not verified",
        "ok dotnet", "ok docker", "ok github", "ok git", "ok clone", "ok filesystem"
    ];

    private static readonly string[] Failure =
    [
        "FAIL networking: enable mirrored WSL networking and restart WSL",
        "FAIL tools: install tmux >= 3.4, curl and flock",
        "FAIL claude: install Claude Code >= 2.1.284 at ~/.local/bin/claude",
        "FAIL dotnet: install the SDK required by global.json",
        "FAIL docker: enable Docker Desktop WSL integration for Ubuntu and start Docker",
        "FAIL github: install gh and run gh auth login --hostname github.com",
        "FAIL git: configure user.name and user.email",
        "FAIL clone: clone aiakos-hq/aiakos into ~/src/aiakos with origin set",
        "FAIL filesystem: keep clone and seat_root on the WSL filesystem outside /mnt"
    ];

    private const string Unknown = "FAIL claude: unknown, check by hand; run ~/.local/bin/claude and /login";

    [Fact]
    public async Task ExactFloorsPassWithoutTouchingPlannedSeatRoot()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        await sandbox.AssertResultAsync();
        Assert.False(Directory.Exists(Path.Combine(sandbox.Home, "aiakos")));
        Assert.Contains("git -C " + sandbox.Home + "/src/aiakos remote get-url origin\n", File.ReadAllText(sandbox.Log), StringComparison.Ordinal);
        Assert.DoesNotContain("remote get-url extra", File.ReadAllText(sandbox.Log), StringComparison.Ordinal);
        Assert.Equal(0, (await sandbox.RunAsync("-n")).Code);
    }

    [Theory]
    [InlineData("wslinfo", "nat", 0)]
    [InlineData("tmux", "tmux 3.3", 1)]
    [InlineData("tmux", "malformed", 1)]
    [InlineData("claude", "2.1.283 (Claude Code)", 2)]
    [InlineData("claude", "unrelated text", 2)]
    [InlineData("dotnet", "10.0.99", 3)]
    [InlineData("dotnet", "10.0.100-preview.1", 3)]
    [InlineData("dotnet", "invalid", 3)]
    public async Task InvalidOutputFailsOnlyItsGroup(string command, string output, int group)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        sandbox.Command(command, output);
        await sandbox.AssertResultAsync(group);
    }

    [Theory]
    [InlineData("tmux", "tmux 3.4a")]
    [InlineData("tmux", "tmux 3.10")]
    [InlineData("claude", "2.1.284 (Claude Code)")]
    [InlineData("claude", "2.2.0 (Claude Code)")]
    [InlineData("dotnet", "10.0.112")]
    [InlineData("dotnet", "10.1.0")]
    public async Task BoundaryAndNewerVersionsPass(string command, string output)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        sandbox.Command(command, output);
        await sandbox.AssertResultAsync();
    }

    [Theory]
    [InlineData("wslinfo", 0)]
    [InlineData("tmux", 1)]
    [InlineData("curl", 1)]
    [InlineData("flock", 1)]
    [InlineData("claude", 2)]
    [InlineData("dotnet", 3)]
    [InlineData("docker", 4)]
    [InlineData("gh", 5)]
    [InlineData("git", 6)]
    public async Task MissingCommandContinuesLaterChecks(string command, int group)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        File.Delete(sandbox.CommandPath(command));
        await sandbox.AssertResultAsync(command == "git" ? [6, 7] : [group]);
    }

    [Theory]
    [InlineData("wslinfo", 0)]
    [InlineData("tmux", 1)]
    [InlineData("claude", 2)]
    [InlineData("dotnet", 3)]
    [InlineData("docker", 4)]
    [InlineData("gh", 5)]
    public async Task CommandFailureSuppressesPrivateOutput(string command, int group)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        sandbox.Command(command, "private-command-data", 1);
        await sandbox.AssertResultAsync(group);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"oauthAccount\": null}")]
    public async Task MissingAccountMarkerIsExplicitlyUnknown(string? account)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        if (account is null) File.Delete(sandbox.Account);
        else File.WriteAllText(sandbox.Account, account);
        var expected = (string[])Success.Clone();
        expected[2] = Unknown;
        await sandbox.AssertLinesAsync(expected, 1);
    }

    [Fact]
    public async Task UnreadableAccountIsUnknownAndVersionFailureTakesPrecedence()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(sandbox.Account, UnixFileMode.None);
        try
        {
            var expected = (string[])Success.Clone();
            expected[2] = Unknown;
            await sandbox.AssertLinesAsync(expected, 1);
            sandbox.Command("claude", "2.1.283");
            await sandbox.AssertResultAsync(2);
        }
        finally
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(sandbox.Account, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task MultilineMarkerAndMultipleFailuresHaveExactOutput()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        File.WriteAllText(sandbox.Account, "{\n\"oauthAccount\" :\n {\"private\":\"sandbox-only\"}\n}\n");
        sandbox.Command("wslinfo", "mirrored ");
        sandbox.Command("docker", "private", 1);
        sandbox.Command("gh", "private", 1);
        await sandbox.AssertResultAsync(0, 4, 5);
    }

    [Theory]
    [InlineData("https://github.com/aiakos-hq/aiakos")]
    [InlineData("https://github.com/aiakos-hq/aiakos.git")]
    [InlineData("git@github.com:aiakos-hq/aiakos")]
    [InlineData("git@github.com:aiakos-hq/aiakos.git")]
    [InlineData("ssh://git@github.com/aiakos-hq/aiakos")]
    [InlineData("ssh://git@github.com/aiakos-hq/aiakos.git")]
    public async Task AllowedOriginWithAdditionalRemotePasses(string origin)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        sandbox.Origin = origin;
        sandbox.Git();
        await sandbox.AssertResultAsync();
    }

    [Theory]
    [InlineData("https://github.com/aiakos-hq/aiakos/")]
    [InlineData("https://fake-credential@github.com/aiakos-hq/aiakos.git")]
    [InlineData("https://github.com/other/repo.git")]
    public async Task InvalidOriginFailsClone(string origin)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        sandbox.Origin = origin;
        sandbox.Git();
        await sandbox.AssertResultAsync(7);
    }

    [Fact]
    public async Task EmptyGitIdentityAndNonWorktreeFailTheirGroups()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        sandbox.Git(identity: "", worktree: "false");
        await sandbox.AssertResultAsync(6, 7);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("aiakos")]
    public async Task SymlinkToMountFailsFilesystem(string directory)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        var path = Path.Combine(sandbox.Home, directory);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateSymbolicLink(path, "/mnt");
        await sandbox.AssertResultAsync(8);
    }

    [Fact]
    public async Task UnresolvableAncestorFailsFilesystem()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        File.WriteAllText(Path.Combine(sandbox.Home, "aiakos"), "not a directory");
        await sandbox.AssertResultAsync(8);
    }

    [Fact]
    public async Task ArgumentsFailBeforeAnyCommandRuns()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "POSIX checker runs only on Linux.");
        using var sandbox = new Sandbox();
        var result = await sandbox.RunAsync(argument: "unexpected");
        Assert.Equal(2, result.Code);
        Assert.Equal("", result.Output);
        Assert.Equal("usage: check-prereqs.sh\n", result.Error);
        Assert.False(File.Exists(sandbox.Log));
    }

    private sealed class Sandbox : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "aiakos-prereqs-" + Guid.NewGuid().ToString("N"));
        private readonly string script;
        private string Bin => Path.Combine(root, "bin");
        public string Home => Path.Combine(root, "home");
        public string Account => Path.Combine(Home, ".claude.json");
        public string Log => Path.Combine(root, "commands.log");
        public string Origin { get; set; } = "https://github.com/aiakos-hq/aiakos.git";

        public Sandbox()
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Aiakos.slnx"))) repository = repository.Parent;
            Assert.NotNull(repository);
            script = Path.Combine(repository.FullName, "rigs", "aiakos-dev", "check-prereqs.sh");
            Directory.CreateDirectory(Bin);
            Directory.CreateDirectory(Path.Combine(Home, ".local", "bin"));
            Directory.CreateDirectory(Path.Combine(Home, "src", "aiakos"));
            File.WriteAllText(Account, "{\"oauthAccount\":{}}\n");
            if (OperatingSystem.IsLinux()) LinkUtilities();
            Command("wslinfo", "mirrored");
            Command("tmux", "tmux 3.4");
            Command("curl", "");
            Command("flock", "");
            Command("claude", "2.1.284 (Claude Code)");
            Command("dotnet", "10.0.112");
            Command("docker", "private docker output");
            Command("gh", "private auth output");
            Git();
        }

        [SupportedOSPlatform("linux")]
        private void LinkUtilities()
        {
            foreach (var utility in new[] { "awk", "sed", "grep", "sort", "cut", "tr", "pwd" })
                File.CreateSymbolicLink(Path.Combine(Bin, utility), "/usr/bin/" + utility);
        }

        public string CommandPath(string command) => command == "claude" ? Path.Combine(Home, ".local", "bin", command) : Path.Combine(Bin, command);
        private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

        public void Command(string command, string output, int code = 0) => WriteCommand(command,
            "printf '%s\\n' " + Quote(output) + "\nprintf '%s\\n' 'private stderr' >&2\nexit " + code + "\n");

        public void Git(string identity = "Sandbox Identity", string worktree = "true") => WriteCommand("git", """
            case "$*" in
              'config user.name'|'config user.email') printf '%s\n' IDENTITY ;;
              *'rev-parse --is-inside-work-tree') printf '%s\n' WORKTREE ;;
              *'remote get-url origin') printf '%s\n' ORIGIN ;;
              *'remote') printf '%s\n' origin extra ;;
              *) exit 99 ;;
            esac
            """.Replace("IDENTITY", Quote(identity), StringComparison.Ordinal)
                .Replace("WORKTREE", Quote(worktree), StringComparison.Ordinal)
                .Replace("ORIGIN", Quote(Origin), StringComparison.Ordinal) + "\n");

        private void WriteCommand(string command, string body)
        {
            var path = CommandPath(command);
            File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\n' " + Quote(command) + "' '" + "\"$*\" >> \"$FAKE_LOG\"\n" + body);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public async Task<(int Code, string Output, string Error)> RunAsync(string? option = null, string? argument = null)
        {
            var start = new ProcessStartInfo("/bin/sh") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            if (option is not null) start.ArgumentList.Add(option);
            start.ArgumentList.Add(script);
            if (argument is not null) start.ArgumentList.Add(argument);
            start.Environment.Clear();
            start.Environment["HOME"] = Home;
            start.Environment["PATH"] = Bin;
            start.Environment["FAKE_LOG"] = Log;
            start.Environment["LC_ALL"] = "C";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            return (process.ExitCode, await output, await error);
        }

        public Task AssertResultAsync(params int[] failures)
        {
            var expected = (string[])Success.Clone();
            foreach (var group in failures) expected[group] = Failure[group];
            return AssertLinesAsync(expected, failures.Length == 0 ? 0 : 1);
        }

        public async Task AssertLinesAsync(string[] expected, int code)
        {
            var result = await RunAsync();
            Assert.Equal(code, result.Code);
            Assert.Equal(string.Join('\n', expected) + "\n", result.Output);
            Assert.Equal("", result.Error);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
