using System.Collections.ObjectModel;
using Google.Protobuf;
using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Harnesses;
using Aiakos.Orchestrator.Seats;
using Aiakos.Spec;

namespace Aiakos.Orchestrator.Harnesses.ClaudeCode;

public sealed class ClaudeCodeAdapter : IHarnessAdapter
{
    private const string ClaudeName = "claude";
    private const string SeatHome = "${AIAKOS_SEAT_HOME}";
    private const string SettingsPath = "aiakos/claude-settings.json";
    private const string RelayPath = "aiakos/bin/aiakos-hook-relay";
    private const string GuidancePath = "projection/CLAUDE.md";
    private const string SkillPrefix = "projection/.claude/skills/";
    private const string InvalidLaunch = "INVALID_LAUNCH";
    private const string InvalidSessionId = "INVALID_SESSION_ID";
    private const string LaunchModeNotSupported = "LAUNCH_MODE_NOT_SUPPORTED";
    private const uint RegularReadWrite = 0x1A4;
    private const uint ExecutableReadOnly = 0x1ED;

    private readonly ClaudeCodeSettings _settings;
    private readonly ClaudeCodeDelivery _delivery;

    public ClaudeCodeAdapter(ClaudeCodeStateProfile profile, ClaudeCodeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        Profile = profile;
        _settings = settings;
        _delivery = new ClaudeCodeDelivery(profile);
    }

    public string Harness => Profile.Harness;

    public ClaudeCodeStateProfile Profile { get; }

    IHarnessStateProfile IHarnessAdapter.Profile => Profile;

    public LaunchSpec BuildLaunch(ResolvedSeatParameters seat, LaunchMode mode, NativeSession session,
        IReadOnlyList<SeatFile> suppliedFiles)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(suppliedFiles);

        if (!Profile.IsValidNativeSessionId(session.Id))
            throw new InvalidOperationException(InvalidSessionId);

        if (mode == LaunchMode.Fork)
            throw new InvalidOperationException(LaunchModeNotSupported);

        if (mode is not (LaunchMode.Fresh or LaunchMode.Resume) || !IsValidSeat(seat))
            throw new InvalidOperationException(InvalidLaunch);

        var files = BuildFiles(seat, suppliedFiles);
        var argv = new List<string>
        {
            ClaudeName,
            mode == LaunchMode.Fresh ? "--session-id" : "--resume",
            session.Id,
            "-n",
            $"{seat.Seat}@{seat.Rig}",
            "--settings",
            $"{SeatHome}/{SettingsPath}",
            "--add-dir",
            $"{SeatHome}/projection"
        };
        if (seat.Model is not null)
        {
            argv.Add("--model");
            argv.Add(seat.Model);
        }

        IReadOnlyDictionary<string, string> environment = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AIAKOS_SEAT"] = $"{seat.Seat}@{seat.Rig}",
                ["CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD"] = "1",
                ["DISABLE_AUTOUPDATER"] = "1"
            });

        return new LaunchSpec(Array.AsReadOnly(argv.ToArray()), environment,
            Array.AsReadOnly(files.ToArray()), Profile.ReadyTimeout,
            new TerminalSize { Columns = 160, Rows = 45 });
    }

    public DeliverySpec BuildDelivery(string authenticatedSender, string body, Guid commandId) =>
        _delivery.BuildDelivery(authenticatedSender, body, commandId);

    private List<SeatFile> BuildFiles(ResolvedSeatParameters seat, IReadOnlyList<SeatFile> suppliedFiles)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<SeatFile>(suppliedFiles.Count + 1);
        var guidanceCount = 0;
        var relayCount = 0;

        foreach (var source in suppliedFiles)
        {
            if (source is null || source.Root != FileRoot.SeatHome || source.Content is null ||
                !IsValidRelativePath(source.Path) || !paths.Add(source.Path))
            {
                throw new InvalidOperationException(InvalidLaunch);
            }

            uint mode;
            if (string.Equals(source.Path, GuidancePath, StringComparison.Ordinal))
            {
                guidanceCount++;
                mode = RegularReadWrite;
            }
            else if (string.Equals(source.Path, RelayPath, StringComparison.Ordinal))
            {
                relayCount++;
                mode = ExecutableReadOnly;
            }
            else if (source.Path.StartsWith(SkillPrefix, StringComparison.Ordinal) &&
                     source.Path.Split('/').Length >= 5)
            {
                mode = RegularReadWrite;
            }
            else
            {
                throw new InvalidOperationException(InvalidLaunch);
            }

            files.Add(CloneFile(source, mode, expand: false));
        }

        if (guidanceCount != 1 || relayCount != 1)
            throw new InvalidOperationException(InvalidLaunch);

        files.Add(new SeatFile
        {
            Root = FileRoot.SeatHome,
            Path = SettingsPath,
            Content = ByteString.CopyFrom(_settings.Build(seat)),
            Mode = RegularReadWrite,
            Expand = true
        });
        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return files;
    }

    private static SeatFile CloneFile(SeatFile source, uint mode, bool expand) => new()
    {
        Root = FileRoot.SeatHome,
        Path = source.Path,
        Content = ByteString.CopyFrom(source.Content.Span),
        Mode = mode,
        Expand = expand
    };

    private static bool IsValidSeat(ResolvedSeatParameters seat)
    {
        if (seat.Harness != "claude-code" || !IsSeatId(seat.Seat) || !IsRigId(seat.Rig) ||
            !IsSafeNodePath(seat.SeatDir) || !IsSafeNodePath(seat.Workdir) ||
            !IsSafeNodePath(seat.ProjectionRoot) ||
            !string.Equals(seat.ProjectionRoot, $"{seat.SeatDir}/projection", StringComparison.Ordinal) ||
            seat.HarnessSettings is null || seat.HarnessSettings.Permissions is null ||
            seat.HarnessSettings.Permissions.Allow is null || seat.HarnessSettings.Permissions.Ask is null ||
            seat.HarnessSettings.Permissions.Deny is null || seat.Secrets is null ||
            seat.Secrets.Any(static secret => secret is null) ||
            seat.Model is not null && !IsValidModel(seat.Model))
        {
            return false;
        }

        return true;
    }

    private static bool IsSeatId(string? value) => IsSchemaId(value, maxLength: 24, minLength: 1);

    private static bool IsRigId(string? value) => IsSchemaId(value, maxLength: 40, minLength: 2);

    private static bool IsSchemaId(string? value, int maxLength, int minLength)
    {
        if (value is null || value.Length < minLength || value.Length > maxLength ||
            value[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in value.AsSpan(1))
        {
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                return false;
        }

        return true;
    }

    private static bool IsSafeNodePath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        string relative;
        if (value[0] == '/')
            relative = value[1..];
        else if (value.StartsWith("~/", StringComparison.Ordinal))
            relative = value[2..];
        else
            return false;

        return IsSafePathRemainder(relative);
    }

    private static bool IsSafePathRemainder(string value)
    {
        if (value.Length == 0 || value.Any(character => !IsSafePathCharacter(character)))
            return false;

        return value.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }

    private static bool IsSafePathCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '/' or '-';

    private static bool IsValidRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains('\\') ||
            path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return false;
        }

        for (var index = 0; index < path.Length; index++)
        {
            var character = path[index];
            if (char.IsControl(character))
                return false;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= path.Length || !char.IsLowSurrogate(path[index + 1]))
                    return false;
                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return false;
            }
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }

    private static bool IsValidModel(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\0')
                return false;
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return false;
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }
}
