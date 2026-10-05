using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Runtime.CompilerServices;
using Aiakos.Core;

[assembly: InternalsVisibleTo("Aiakos.Node.Tests")]

namespace Aiakos.Node.Sessions;

public interface ISessionRegistry
{
    Task<RegistryEntry?> ReadAsync(string sessionName, CancellationToken ct);
    Task<IReadOnlyList<RegistryEntry>> ReadAllAsync(CancellationToken ct);
    Task WriteAsync(RegistryEntry entry, CancellationToken ct);
    Task DeleteAsync(string sessionName, CancellationToken ct);
    Task<IReadOnlyList<RegistryEntry>> ReadRecoveryAsync(string sessionName, CancellationToken ct);
    Task WriteRecoveryAsync(RegistryEntry entry, CancellationToken ct);
    Task DeleteRecoveryAsync(string sessionName, CancellationToken ct);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(RegistryEntry))]
[JsonSerializable(typeof(IReadOnlyList<RegistryEntry>))]
[JsonSerializable(typeof(RegistryEntry[]))]
internal sealed partial class SessionRegistryJsonContext : JsonSerializerContext;

public sealed class SessionRegistry : ISessionRegistry
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly Action? _beforeRename;

    public SessionRegistry(string home)
        : this(home, null)
    {
    }

    internal SessionRegistry(string home, Action? beforeRename)
    {
        if (!Path.IsPathFullyQualified(home))
            throw new ArgumentException("Invalid tmux host options.");

        _directory = Path.Combine(home, "sessions");
        _beforeRename = beforeRename;
        Directory.CreateDirectory(_directory);
        SetMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public async Task<RegistryEntry?> ReadAsync(string sessionName, CancellationToken ct)
    {
        ValidateName(sessionName);
        return await ReadOneAsync(PathFor(sessionName), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RegistryEntry>> ReadAllAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var entries = new List<RegistryEntry>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(path);
                ValidateName(name);
                var entry = await ReadOneAsync(path, ct).ConfigureAwait(false);
                if (entry is not null) entries.Add(entry);
            }
            return entries;
        }
        catch (OperationCanceledException) { throw; }
        catch (SessionHostException) { throw; }
        catch (Exception) { throw Failed(); }
    }

    public Task WriteAsync(RegistryEntry entry, CancellationToken ct)
    {
        ValidateEntry(entry);
        return LockedAsync(entry.SessionName, () => WriteAtomicAsync(PathFor(entry.SessionName), entry, ct), ct);
    }

    public Task DeleteAsync(string sessionName, CancellationToken ct)
    {
        ValidateName(sessionName);
        return LockedAsync(sessionName, () => DeleteCoreAsync(PathFor(sessionName), ct), ct);
    }

    public async Task<IReadOnlyList<RegistryEntry>> ReadRecoveryAsync(string sessionName, CancellationToken ct)
    {
        ValidateName(sessionName);
        var path = RecoveryPathFor(sessionName);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return Array.Empty<RegistryEntry>();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var entries = await JsonSerializer.DeserializeAsync(stream, SessionRegistryJsonContext.Default.IReadOnlyListRegistryEntry, ct).ConfigureAwait(false);
            return entries ?? throw Failed();
        }
        catch (OperationCanceledException) { throw; }
        catch (SessionHostException) { throw; }
        catch (Exception) { throw Failed(); }
    }

    public Task WriteRecoveryAsync(RegistryEntry entry, CancellationToken ct)
    {
        ValidateEntry(entry);
        return LockedAsync(entry.SessionName, async () =>
        {
            IReadOnlyList<RegistryEntry> existing;
            try { existing = await ReadRecoveryAsync(entry.SessionName, ct).ConfigureAwait(false); }
            catch (SessionHostException) { throw; }
            if (existing.Any(item => item.LaunchId == entry.LaunchId)) return;
            await WriteAtomicAsync(RecoveryPathFor(entry.SessionName), existing.Append(entry).ToArray(), ct).ConfigureAwait(false);
        }, ct);
    }

    public Task DeleteRecoveryAsync(string sessionName, CancellationToken ct)
    {
        ValidateName(sessionName);
        return LockedAsync(sessionName, () => DeleteCoreAsync(RecoveryPathFor(sessionName), ct), ct);
    }

    private static async Task<RegistryEntry?> ReadOneAsync(string path, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await JsonSerializer.DeserializeAsync(stream, SessionRegistryJsonContext.Default.RegistryEntry, ct).ConfigureAwait(false)
                ?? throw Failed();
        }
        catch (OperationCanceledException) { throw; }
        catch (SessionHostException) { throw; }
        catch (Exception) { throw Failed(); }
    }

    private async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        string? temporary = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            temporary = Path.Combine(_directory, $".{Guid.NewGuid():N}.tmp");
            var bytes = value switch
            {
                RegistryEntry entry => JsonSerializer.SerializeToUtf8Bytes(entry, SessionRegistryJsonContext.Default.RegistryEntry),
                RegistryEntry[] entries => JsonSerializer.SerializeToUtf8Bytes(entries, SessionRegistryJsonContext.Default.RegistryEntryArray),
                _ => throw new InvalidOperationException()
            };
            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.Asynchronous
            };
            if (OperatingSystem.IsLinux())
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, fileOptions))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            _beforeRename?.Invoke();
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
            temporary = null;
            SetMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw Failed(); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch (IOException) { }
            }
        }
    }

    private static async Task DeleteCoreAsync(string path, CancellationToken ct)
    {
        try { ct.ThrowIfCancellationRequested(); File.Delete(path); await Task.CompletedTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw Failed(); }
    }

    private async Task LockedAsync(string name, Func<Task> action, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(Path.Combine(_directory, name), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { await action().ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private string PathFor(string name) => Path.Combine(_directory, name + ".json");
    private string RecoveryPathFor(string name) => Path.Combine(_directory, name + ".recovery");

    private static void ValidateEntry(RegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateName(entry.SessionName);
        if (!string.Equals(entry.SessionName, TmuxNames.SessionName(entry.SeatAddress), StringComparison.Ordinal))
            throw new ArgumentException("Invalid registry session name.");
    }

    private static void ValidateName(string name)
    {
        if (name is null || name.Length is < 3 or > 65 || name[0] is < 'a' or > 'z' || name.Count(c => c == '_') != 1)
            throw new ArgumentException("Invalid registry session name.");
        var parts = name.Split('_');
        if (parts[0].Length is < 2 or > 40 || parts[1].Length is < 1 or > 24 ||
            parts[0][0] is < 'a' or > 'z' || parts[1][0] is < 'a' or > 'z' ||
            parts.Any(part => part.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')))
            throw new ArgumentException("Invalid registry session name.");
    }

    private static void SetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, mode);
    }

    private static SessionHostException Failed() => new(new SessionHostError(SessionHostErrorCode.TmuxFailed,
        "Session registry operation failed.", true));
}
