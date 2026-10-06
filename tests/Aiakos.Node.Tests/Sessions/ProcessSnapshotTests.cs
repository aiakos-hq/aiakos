using System.Globalization;
using System.Text;
using Aiakos.Node.Sessions;

namespace Aiakos.Node.Tests.Sessions;

public sealed class ProcessSnapshotTests
{
    private static readonly int[] SortedPids = [9, 30];
    private static readonly string[] ReplacedInvalidArgv = ["a", "�"];
    private static readonly int[] ProbedPids = [30, 42, 99];
    [Fact]
    public async Task ReadsNumericProcEntriesForEffectiveUidInPidOrder()
    {
        using var fixture = new ProcFixture();
        fixture.Add(30, 1000, Stat(30, "worker (with) spaces", 4, 7, 9876), [0x61, 0, 0xff, 0]);
        fixture.Add(9, 1000, Stat(9, "short", 1, 2, 12), [0x78, 0]);
        fixture.Add(5, 2000, "malformed", [0xff]);
        fixture.Add(40, 1000, null, []); // Matching UID, but the process vanished before stat/cmdline reads.
        Directory.CreateDirectory(Path.Combine(fixture.Root, "not-a-pid"));

        var snapshot = await new ProcessSnapshot(fixture.Root, 1000).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SortedPids, snapshot.Select(process => process.Pid));
        Assert.Equal((1, 2, 12UL), (snapshot[0].ParentPid, snapshot[0].SessionId, snapshot[0].StartTime));
        Assert.Equal((4, 7, 9876UL), (snapshot[1].ParentPid, snapshot[1].SessionId, snapshot[1].StartTime));
        Assert.Equal(ReplacedInvalidArgv, snapshot[1].Argv);
    }

    [Fact]
    public async Task MatchingUidStatParseFailureHasFixedRetryableError()
    {
        using var fixture = new ProcFixture();
        fixture.Add(17, 1000, "not a stat record", []);

        var error = await Assert.ThrowsAsync<SessionHostException>(() =>
            new ProcessSnapshot(fixture.Root, 1000).ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal((SessionHostErrorCode.TmuxFailed, "Process snapshot could not be read.", true),
            (error.Code, error.Message, error.Error.Retryable));
    }

    [Fact]
    public void ExcludesManagedTreesAndSessionsAndProbesRemainingPidsInOrder()
    {
        var processes = new[]
        {
            Process(99, 1, 99), Process(12, 1, 10), Process(42, 1, 42), Process(11, 10, 10),
            Process(30, 1, 30), Process(10, 1, 10)
        };
        var probe = new RecordingProbe(process => process.Pid is 42 or 99);

        var error = Assert.Throws<SessionHostException>(() => ProcessSnapshot.CheckOrphans(processes, [10], probe));

        Assert.Equal((SessionHostErrorCode.OrphanDetected, "Orphan harness processes detected.", false),
            (error.Code, error.Message, error.Error.Retryable));
        Assert.Equal("42,99", error.Error.Metadata!["pids"]);
        Assert.Equal(ProbedPids, probe.SeenPids);
    }

    [Fact]
    public void NullProbeIsNotInvokedAndProbeFailureDoesNotLeakItsMessage()
    {
        var processes = new[] { Process(42, 1, 42) };
        ProcessSnapshot.CheckOrphans(processes, [], null);

        var error = Assert.Throws<SessionHostException>(() =>
            ProcessSnapshot.CheckOrphans(processes, [], new RecordingProbe(_ => throw new InvalidOperationException("sensitive probe detail"))));

        Assert.Equal((SessionHostErrorCode.TmuxFailed, "Orphan probe failed.", true),
            (error.Code, error.Message, error.Error.Retryable));
        Assert.DoesNotContain("sensitive", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresAnObservedPaneStartTimeAndNeverGuesses()
    {
        var snapshot = new[] { Process(1234, 1, 42) with { StartTime = 9876 } };
        Assert.Equal(9876UL, ProcessSnapshot.RequireStartTime(snapshot, 1234));

        var error = Assert.Throws<SessionHostException>(() => ProcessSnapshot.RequireStartTime(snapshot, 5678));
        Assert.Equal((SessionHostErrorCode.TmuxFailed, "Created pane process could not be identified.", true),
            (error.Code, error.Message, error.Error.Retryable));
    }

    private static ProcessInfo Process(int pid, int parent, int session) => new(pid, parent, session, (ulong)pid, []);

    private static string Stat(int pid, string comm, int parent, int session, ulong startTime)
    {
        var fields = new[] { "S", parent.ToString(CultureInfo.InvariantCulture), "1", session.ToString(CultureInfo.InvariantCulture),
            "0", "-1", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "1", "0",
            startTime.ToString(CultureInfo.InvariantCulture) };
        return $"{pid} ({comm}) {string.Join(' ', fields)}";
    }

    private sealed class RecordingProbe(Func<ProcessInfo, bool> match) : IOrphanProbe
    {
        private readonly List<int> _seenPids = [];
        public IReadOnlyList<int> SeenPids => _seenPids;
        public bool Matches(ProcessInfo process)
        {
            _seenPids.Add(process.Pid);
            return match(process);
        }
    }

    private sealed class ProcFixture : IDisposable
    {
        public ProcFixture() => Root = Path.Combine(Path.GetTempPath(), "aiakos-proc-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }

        public void Add(int pid, uint uid, string? stat, byte[] commandLine)
        {
            var directory = Path.Combine(Root, pid.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "status"), $"Name:\ttest\nUid:\t{uid}\t{uid}\t{uid}\t{uid}\n");
            if (stat is not null) File.WriteAllText(Path.Combine(directory, "stat"), stat);
            File.WriteAllBytes(Path.Combine(directory, "cmdline"), commandLine);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
