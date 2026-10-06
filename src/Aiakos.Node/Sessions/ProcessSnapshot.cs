using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Aiakos.Node.Sessions;

public interface IProcessSnapshot
{
    Task<IReadOnlyList<ProcessInfo>> ReadAsync(CancellationToken ct);
}

public sealed partial class ProcessSnapshot : IProcessSnapshot
{
    private const string ReadFailureMessage = "Process snapshot could not be read.";
    private readonly string _procRoot;
    private readonly uint _effectiveUid;

    public ProcessSnapshot(string procRoot = "/proc", uint? effectiveUid = null)
    {
        _procRoot = procRoot;
        _effectiveUid = effectiveUid ?? (OperatingSystem.IsLinux()
            ? GetEffectiveUid()
            : throw new PlatformNotSupportedException("The effective UID is available only on Linux."));
    }

    public Task<IReadOnlyList<ProcessInfo>> ReadAsync(CancellationToken ct)
    {
        var processes = new List<ProcessInfo>();
        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var directory in Directory.EnumerateDirectories(_procRoot))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
                    continue;

                try
                {
                    var uid = ReadEffectiveUid(Path.Combine(directory, "status"));
                    if (uid != _effectiveUid) continue;

                    var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                    var parsed = ParseStat(stat, pid);
                    var commandLine = File.ReadAllBytes(Path.Combine(directory, "cmdline"));
                    processes.Add(new ProcessInfo(pid, parsed.ParentPid, parsed.SessionId, parsed.StartTime,
                        ParseCommandLine(commandLine)));
                }
                catch (FileNotFoundException) { /* A proc entry can disappear between enumeration and read. */ }
                catch (DirectoryNotFoundException) { /* The process exited while its proc files were read. */ }
            }
            return Task.FromResult<IReadOnlyList<ProcessInfo>>(processes.OrderBy(process => process.Pid).ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw ReadFailed(); }
    }

    public static void CheckOrphans(IReadOnlyList<ProcessInfo> snapshot, IReadOnlyList<int> livePanePids, IOrphanProbe? probe)
    {
        if (probe is null) return;

        var roots = livePanePids.ToHashSet();
        var liveSessionIds = snapshot.Where(process => roots.Contains(process.Pid))
            .Select(process => process.SessionId).ToHashSet();
        var excluded = new HashSet<int>(roots);
        foreach (var process in snapshot)
            if (liveSessionIds.Contains(process.SessionId)) excluded.Add(process.Pid);

        var added = true;
        while (added)
        {
            added = false;
            foreach (var process in snapshot)
                if (!excluded.Contains(process.Pid) && excluded.Contains(process.ParentPid))
                    added |= excluded.Add(process.Pid);
        }

        var matches = new List<int>();
        try
        {
            foreach (var process in snapshot.OrderBy(process => process.Pid))
                if (!excluded.Contains(process.Pid) && probe.Matches(process))
                    matches.Add(process.Pid);
        }
        catch (Exception) { throw new SessionHostException(new SessionHostError(SessionHostErrorCode.TmuxFailed,
            "Orphan probe failed.", true)); }

        if (matches.Count > 0)
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.OrphanDetected,
                "Orphan harness processes detected.", false,
                new Dictionary<string, string> { ["pids"] = string.Join(',', matches) }));
    }

    public static ulong RequireStartTime(IReadOnlyList<ProcessInfo> snapshot, int panePid)
    {
        foreach (var process in snapshot)
            if (process.Pid == panePid) return process.StartTime;
        throw new SessionHostException(new SessionHostError(SessionHostErrorCode.TmuxFailed,
            "Created pane process could not be identified.", true));
    }

    private static uint ReadEffectiveUid(string statusPath)
    {
        foreach (var line in File.ReadLines(statusPath))
        {
            if (!line.StartsWith("Uid:", StringComparison.Ordinal)) continue;
            var fields = line[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
                return uid;
            throw new FormatException();
        }
        throw new FormatException();
    }

    private static (int ParentPid, int SessionId, ulong StartTime) ParseStat(string stat, int expectedPid)
    {
        var open = stat.IndexOf('(');
        var close = stat.LastIndexOf(')');
        if (open <= 0 || close <= open ||
            !int.TryParse(stat.AsSpan(0, open).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            pid != expectedPid)
            throw new FormatException();

        var fields = stat[(close + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= 19 || fields[0].Length != 1 ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentPid) ||
            !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionId) ||
            !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var startTime))
            throw new FormatException();
        return (parentPid, sessionId, startTime);
    }

    private static string[] ParseCommandLine(byte[] bytes)
    {
        var arguments = Encoding.UTF8.GetString(bytes).Split('\0');
        if (arguments.Length > 0 && arguments[^1].Length == 0)
            Array.Resize(ref arguments, arguments.Length - 1);
        return arguments;
    }

    private static SessionHostException ReadFailed() => new(new SessionHostError(SessionHostErrorCode.TmuxFailed,
        ReadFailureMessage, true));

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
}
