using System.Net.Sockets;
using System.Runtime.InteropServices;

using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class SpecialFileTests
{
    private static readonly ReferenceSource ProbeSource = new("probe.yaml", 7, 9);

    [Fact]
    public void FileReferencesRejectFifosSocketsAndDevicesButAcceptRegularFiles()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix special-file checks require Linux.");
        WithRoot(root =>
        {
            var fifoPath = Path.Combine(root, "pipe");
            CreateFifo(fifoPath);
            var socketPath = Path.Combine(root, "socket");
            using var socket = CreateBoundSocket(socketPath);
            var regularPath = Path.Combine(root, "regular");
            File.WriteAllText(regularPath, "safe");

            AssertMissing(root, "pipe");
            AssertMissing(root, "socket");
            AssertMissing("/dev", "null");

            var diagnostics = new List<Diagnostic>();
            var regular = SharedReferencePaths.Resolve(root, "", "regular", SharedReferenceKind.File,
                ProbeSource, diagnostics);
            Assert.Equal("regular", regular?.Path);
            Assert.Empty(diagnostics);
        });
    }

    [Fact]
    public void FileReferencesRejectAnAccessibleBlockDeviceWhenOneExists()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix special-file checks require Linux.");
        var blockDevice = Directory.EnumerateFileSystemEntries("/dev").FirstOrDefault(IsAccessibleBlockDevice);
        if (blockDevice is null)
            Assert.Skip("No accessible block device is present under /dev on this host.");

        AssertMissing("/dev", Path.GetFileName(blockDevice));
    }

    [Fact]
    public async Task AgentDiscoveryRejectsAFifoWithoutOpeningOrWaitingForIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix FIFO checks require Linux.");
        var root = CopyMinimalRig();
        var agentPath = Path.Combine(root, "agents", "impl", "agent.yaml");
        try
        {
            File.Delete(agentPath);
            CreateFifo(agentPath);

            var result = await CompleteWithoutFifoRead(() => RigLoader.Load(root, null), agentPath);

            Assert.Null(result.Rig);
            Assert.Equal("rig.yaml:10:16: error AIK3001: referenced file or directory not found\n",
                DiagnosticFormatter.Format(result.Diagnostics));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentDiscoveryRejectsASocketBeforeTryingToReadIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix socket checks require Linux.");
        var root = CopyMinimalRig();
        var agentPath = Path.Combine(root, "agents", "impl", "agent.yaml");
        try
        {
            File.Delete(agentPath);
            using var socket = CreateBoundSocket(agentPath);

            var result = RigLoader.Load(root, null);

            Assert.Null(result.Rig);
            Assert.Equal("rig.yaml:10:16: error AIK3001: referenced file or directory not found\n",
                DiagnosticFormatter.Format(result.Diagnostics));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SkillEnumerationRejectsAFifoSkillFileWithoutOpeningOrWaitingForIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix FIFO checks require Linux.");
        await WithRootAsync(async root =>
        {
            var directory = CreateSkillDirectory(root);
            var fifoPath = Path.Combine(directory.AbsolutePath, "SKILL.md");
            CreateFifo(fifoPath);
            var diagnostics = new List<Diagnostic>();

            var skill = await CompleteWithoutFifoRead(
                () => SharedSkillReader.Read(root, directory, ProbeSource, diagnostics), fifoPath);

            Assert.Null(skill);
            Assert.Equal("probe.yaml:7:9: error AIK3001: referenced file or directory not found\n",
                DiagnosticFormatter.Format(diagnostics));
        });
    }

    [Fact]
    public void SkillEnumerationRejectsASocketEntryBeforeTryingToReadIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix socket checks require Linux.");
        WithRoot(root =>
        {
            var directory = CreateSkillDirectory(root);
            File.WriteAllText(Path.Combine(directory.AbsolutePath, "SKILL.md"),
                "---\nname: build\ndescription: builds\n---\n");
            using var socket = CreateBoundSocket(Path.Combine(directory.AbsolutePath, "socket-entry"));

            var diagnostics = new List<Diagnostic>();
            Assert.Null(SharedSkillReader.Read(root, directory, ProbeSource, diagnostics));
            Assert.Equal("probe.yaml:7:9: error AIK3001: referenced file or directory not found\n",
                DiagnosticFormatter.Format(diagnostics));
        });
    }

    [Fact]
    public void SkillEnumerationRejectsADeviceEntryBeforeTryingToReadIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix device checks require Linux.");
        WithRoot(root =>
        {
            var directory = CreateSkillDirectory(root);
            File.WriteAllText(Path.Combine(directory.AbsolutePath, "SKILL.md"),
                "---\nname: build\ndescription: builds\n---\n");
            var devicePath = Path.Combine(directory.AbsolutePath, "device-entry");
            if (!TryCreateCharacterDevice(devicePath))
                Assert.Skip("This host does not allow creating a test device entry.");

            var diagnostics = new List<Diagnostic>();
            Assert.Null(SharedSkillReader.Read(root, directory, ProbeSource, diagnostics));
            Assert.Equal("probe.yaml:7:9: error AIK3001: referenced file or directory not found\n",
                DiagnosticFormatter.Format(diagnostics));
        });
    }

    [Fact]
    public void UnreadableAgentFileReportsAtEveryAliasReferenceWithoutFileDiagnostics()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Unix permission checks require Linux.");
            return;
        }
        if (GetEffectiveUserId() == 0)
            Assert.Skip("Permission-denial cases require a non-root user.");

        var root = CopyMinimalRig();
        var rigPath = Path.Combine(root, "rig.yaml");
        var agentPath = Path.Combine(root, "agents", "impl", "agent.yaml");
        var originalMode = File.GetUnixFileMode(agentPath);
        try
        {
            File.AppendAllText(rigPath,
                "  - id: impl2\n" +
                "    agent_ref: local:agents/./impl\n" +
                "    checkout: shared\n");
            File.SetUnixFileMode(agentPath, UnixFileMode.None);

            var result = RigLoader.Load(root, null);

            Assert.Null(result.Rig);
            Assert.Equal(
                new[] { ("rig.yaml", 10, 16), ("rig.yaml", 13, 16) },
                result.Diagnostics.Where(diagnostic => diagnostic.Code == "AIK3001")
                    .Select(diagnostic => (diagnostic.File, diagnostic.Line, diagnostic.Column)));
            Assert.DoesNotContain(result.Diagnostics,
                diagnostic => diagnostic.Code == "AIK1001" && diagnostic.File.StartsWith("agents/", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(agentPath, originalMode);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertMissing(string rigRoot, string relativePath)
    {
        var diagnostics = new List<Diagnostic>();
        Assert.Null(SharedReferencePaths.Resolve(rigRoot, "", relativePath, SharedReferenceKind.File,
            ProbeSource, diagnostics));
        Assert.Equal("probe.yaml:7:9: error AIK3001: referenced file or directory not found\n",
            DiagnosticFormatter.Format(diagnostics));
    }

    private static SharedPath CreateSkillDirectory(string root)
    {
        var absolutePath = Path.Combine(root, "agents", "impl", "skills", "build");
        Directory.CreateDirectory(absolutePath);
        return new SharedPath(absolutePath, "agents/impl/skills/build");
    }

    private static Socket CreateBoundSocket(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        return socket;
    }

    private static void CreateFifo(string path)
    {
        if (MakeFifoNative(path, 0x1B6) != 0)
            throw new IOException($"mkfifo failed with errno {Marshal.GetLastPInvokeError()}.");
    }

    private static int MakeFifoNative(string path, uint mode)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(path);
        try { return MakeFifo(utf8Path, mode); }
        finally { Marshal.FreeCoTaskMem(utf8Path); }
    }

    private static bool TryCreateCharacterDevice(string path)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(path);
        try { return MakeNode(utf8Path, 0x2000 | 0x1B6, 0x103) == 0; }
        finally { Marshal.FreeCoTaskMem(utf8Path); }
    }

    private static async Task<T> CompleteWithoutFifoRead<T>(Func<T> operation, string fifoPath)
    {
        var operationTask = Task.Run(operation);
        if (await Task.WhenAny(operationTask, Task.Delay(TimeSpan.FromSeconds(5))) == operationTask)
            return await operationTask;

        var cleanupWriter = Task.Run(() =>
        {
            using var writer = new FileStream(fifoPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        });
        await cleanupWriter.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await operationTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // The timeout itself is the test failure; this await only ensures cleanup completes.
        }

        Assert.Fail("The loader opened a FIFO and waited for a writer.");
        throw new InvalidOperationException("Unreachable after failing the FIFO no-read assertion.");
    }

    private static bool IsAccessibleBlockDevice(string path)
    {
        var stat = new byte[256];
        if (StatxNative(-100, path, 0x100, 1, stat) != 0)
            return false;
        var mask = BitConverter.ToUInt32(stat, 0);
        var mode = BitConverter.ToUInt16(stat, 28);
        return (mask & 1) != 0 && (mode & 0xF000) == 0x6000;
    }

    private static int StatxNative(int directoryFileDescriptor, string path, int flags, uint mask,
        byte[] buffer)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(path);
        try { return Statx(directoryFileDescriptor, utf8Path, flags, mask, buffer); }
        finally { Marshal.FreeCoTaskMem(utf8Path); }
    }

    private static string CopyMinimalRig()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "minimal");
        var destination = Path.Combine(Path.GetTempPath(), $"aiakos-special-rig-{Guid.NewGuid():N}");
        CopyDirectory(source, destination);
        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-special-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task WithRootAsync(Func<string, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-special-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { await action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(IntPtr path, uint mode);

    [DllImport("libc", EntryPoint = "mknod", SetLastError = true)]
    private static extern int MakeNode(IntPtr path, uint mode, ulong device);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directoryFileDescriptor, IntPtr path, int flags, uint mask,
        [Out] byte[] buffer);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
