using System.Globalization;

namespace Aiakos.Node.Tests;

public sealed class InstanceLockTests
{
    [Fact]
    public void AcquireCreatesTheHomeAndWritesPidAndStartTime()
    {
        using var home = new TempDirectory();

        Assert.True(InstanceLock.TryAcquire(home.Path, out var instanceLock, out _));
        using (instanceLock)
        {
            Assert.Equal(Path.Combine(home.Path, InstanceLock.FileName), instanceLock.FilePath);
            Assert.Equal(Environment.ProcessId, InstanceLock.ReadHolderPid(instanceLock.FilePath));
        }

        var lines = File.ReadAllLines(Path.Combine(home.Path, InstanceLock.FileName));
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), lines[0]);
        Assert.True(DateTimeOffset.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
    }

    [Fact]
    public void SecondLockOnTheSameFileFailsAndReportsTheFirstPid()
    {
        using var home = new TempDirectory();
        Assert.True(InstanceLock.TryAcquire(home.Path, out var first, out _));
        using (first)
        {
            Assert.False(InstanceLock.TryAcquire(home.Path, out var second, out var holderPid));

            Assert.Null(second);
            Assert.Equal(Environment.ProcessId, holderPid);
        }
    }

    [Fact]
    public void LockIsFreeAfterTheFirstIsDisposed()
    {
        using var home = new TempDirectory();
        Assert.True(InstanceLock.TryAcquire(home.Path, out var first, out _));
        first.Dispose();

        Assert.True(InstanceLock.TryAcquire(home.Path, out var second, out _));
        second.Dispose();
    }

    [Fact]
    public void HolderPidIsNullWhenTheFileNamesNone()
    {
        using var home = new TempDirectory();
        Directory.CreateDirectory(home.Path);
        var path = Path.Combine(home.Path, InstanceLock.FileName);
        File.WriteAllText(path, "");

        Assert.Null(InstanceLock.ReadHolderPid(path));
        Assert.Null(InstanceLock.ReadHolderPid(Path.Combine(home.Path, "missing.lock")));
    }

    [Fact]
    public async Task NodeExits3NamingTheLockFileAndHolderPidWhenTheLockIsHeld()
    {
        using var home = new TempDirectory();
        using var logs = new CapturingLoggerProvider();
        Assert.True(InstanceLock.TryAcquire(home.Path, out var holder, out _));
        using (holder)
        {
            var code = await NodeProgram.RunAsync(
                [],
                NodeHost.With(NodeHost.ValidSettings(home.Path), logs),
                TestContext.Current.CancellationToken);

            Assert.Equal(NodeExitCodes.LockHeld, code);
            Assert.Contains(holder.FilePath, logs.Text, StringComparison.Ordinal);
            Assert.Contains(InstanceLock.FileName, logs.Text, StringComparison.Ordinal);
            Assert.Contains($"pid {Environment.ProcessId}", logs.Text, StringComparison.Ordinal);
        }
    }
}
