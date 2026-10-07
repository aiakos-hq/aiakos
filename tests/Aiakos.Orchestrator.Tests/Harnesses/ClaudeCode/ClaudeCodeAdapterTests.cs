using Google.Protobuf;
using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Harnesses;
using Aiakos.Orchestrator.Harnesses.ClaudeCode;
using Aiakos.Spec;
using Xunit;

namespace Aiakos.Orchestrator.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeCodeAdapterTests
{
    private const string NativeId = "11111111-1111-4111-8111-111111111111";
    private const string LowercaseHexNativeId = "abcdefab-cdef-4abc-8def-abcdefabcdef";
    private static readonly Guid CommandId = Guid.Parse("12345678-1234-4123-8123-123456789abc");
    private static readonly string[] ExpectedFilePaths =
    [
        "aiakos/bin/aiakos-hook-relay", "aiakos/claude-settings.json",
        "projection/.claude/skills/probe/SKILL.md", "projection/CLAUDE.md"
    ];

    [Fact]
    public void BuildsFreshAndResumeLaunchesWithExactCommandAndIndependentSortedFiles()
    {
        var profile = new ClaudeCodeStateProfile();
        var adapter = new ClaudeCodeAdapter(profile, new ClaudeCodeSettings());
        var seat = Seat();
        var files = SuppliedFiles();

        var fresh = adapter.BuildLaunch(seat, LaunchMode.Fresh, new NativeSession(NativeId), files);

        Assert.Equal(new[]
        {
            "claude", "--session-id", NativeId, "-n", "impl@aiakos-dev", "--settings",
            "${AIAKOS_SEAT_HOME}/aiakos/claude-settings.json", "--add-dir",
            "${AIAKOS_SEAT_HOME}/projection"
        }, fresh.Argv);
        Assert.Equal(new Dictionary<string, string>
        {
            ["AIAKOS_SEAT"] = "impl@aiakos-dev",
            ["CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD"] = "1",
            ["DISABLE_AUTOUPDATER"] = "1"
        }, fresh.Env);
        Assert.Equal(TimeSpan.FromSeconds(15), fresh.ReadyTimeout);
        Assert.Equal((160U, 45U), (fresh.Terminal.Columns, fresh.Terminal.Rows));
        Assert.Equal(ExpectedFilePaths, fresh.Files.Select(file => file.Path));
        Assert.All(fresh.Files, file => Assert.Equal(FileRoot.SeatHome, file.Root));
        Assert.Equal((0x1A4U, false), (fresh.Files.Single(file => file.Path == "projection/CLAUDE.md").Mode,
            fresh.Files.Single(file => file.Path == "projection/CLAUDE.md").Expand));
        Assert.Equal((0x1EDU, false), (fresh.Files.Single(file => file.Path == "aiakos/bin/aiakos-hook-relay").Mode,
            fresh.Files.Single(file => file.Path == "aiakos/bin/aiakos-hook-relay").Expand));
        Assert.Equal((0x1A4U, false), (fresh.Files.Single(file => file.Path == "projection/.claude/skills/probe/SKILL.md").Mode,
            fresh.Files.Single(file => file.Path == "projection/.claude/skills/probe/SKILL.md").Expand));
        Assert.Equal((0x1A4U, true), (fresh.Files.Single(file => file.Path == "aiakos/claude-settings.json").Mode,
            fresh.Files.Single(file => file.Path == "aiakos/claude-settings.json").Expand));
        Assert.Equal("guidance\n", fresh.Files.Single(file => file.Path == "projection/CLAUDE.md").Content.ToStringUtf8());
        Assert.Equal(new byte[] { 0, 255, 1 }, fresh.Files.Single(file => file.Path == "projection/.claude/skills/probe/SKILL.md")
            .Content.ToByteArray());
        Assert.Equal(new ClaudeCodeSettings().Build(seat),
            fresh.Files.Single(file => file.Path == "aiakos/claude-settings.json").Content.ToByteArray());

        var clonedGuidance = fresh.Files.Single(file => file.Path == "projection/CLAUDE.md");
        files[0].Path = "caller-mutated";
        files[0].Content = ByteString.CopyFromUtf8("caller mutation");
        fresh.Files[0].Path = "result-mutated";
        fresh.Files[0].Content = ByteString.CopyFromUtf8("result mutation");
        Assert.Equal("projection/CLAUDE.md", clonedGuidance.Path);
        Assert.Equal("guidance\n", clonedGuidance.Content.ToStringUtf8());
        Assert.Equal("aiakos/bin/aiakos-hook-relay", files[2].Path);
        Assert.Equal("relay-fixture\n", files[2].Content.ToStringUtf8());
        var resume = adapter.BuildLaunch(seat with { Model = "" }, LaunchMode.Resume,
            new NativeSession(NativeId), SuppliedFiles());

        Assert.Equal(new[]
        {
            "claude", "--resume", NativeId, "-n", "impl@aiakos-dev", "--settings",
            "${AIAKOS_SEAT_HOME}/aiakos/claude-settings.json", "--add-dir",
            "${AIAKOS_SEAT_HOME}/projection", "--model", ""
        }, resume.Argv);
        Assert.DoesNotContain("--session-id", resume.Argv);
        Assert.Equal(ExpectedFilePaths, resume.Files.Select(file => file.Path));
        Assert.Equal("relay-fixture\n", resume.Files.Single(file => file.Path == "aiakos/bin/aiakos-hook-relay").Content.ToStringUtf8());
    }

    [Fact]
    public void AcceptsTildeAnchoredPathsAndUsesOnlySeatHomePlaceholders()
    {
        var seat = Seat(seatDir: "~/aiakos/seats/impl", workdir: "~/aiakos/seats/impl/repos/app",
            projectionRoot: "~/aiakos/seats/impl/projection");

        var launch = Adapter().BuildLaunch(seat, LaunchMode.Fresh, new NativeSession(NativeId), SuppliedFiles());

        Assert.Contains("${AIAKOS_SEAT_HOME}/projection", launch.Argv);
        Assert.DoesNotContain("~/aiakos/seats/impl", launch.Argv);
        Assert.DoesNotContain("/home/", launch.Argv);
        Assert.Equal(new[] { "claude", "--session-id", NativeId }, launch.Argv.Take(3));
    }

    [Fact]
    public void NullTopLevelArgumentsNameTheirParameters()
    {
        var adapter = Adapter();
        Assert.Equal("seat", Assert.Throws<ArgumentNullException>(() => adapter.BuildLaunch(null!, LaunchMode.Fresh,
            new NativeSession(NativeId), SuppliedFiles())).ParamName);
        Assert.Equal("session", Assert.Throws<ArgumentNullException>(() => adapter.BuildLaunch(Seat(), LaunchMode.Fresh,
            null!, SuppliedFiles())).ParamName);
        Assert.Equal("suppliedFiles", Assert.Throws<ArgumentNullException>(() => adapter.BuildLaunch(Seat(),
            LaunchMode.Fresh, new NativeSession(NativeId), null!)).ParamName);
    }

    [Fact]
    public void PreservesUnicodeModelAsOneArgumentAndDoesNotMutateResolvedSnapshot()
    {
        const string model = "odd-\uFFFE-\U0001F680";
        var seat = Seat(model: model);
        var originalPermissions = seat.HarnessSettings.Permissions.Allow.ToArray();
        var adapter = new ClaudeCodeAdapter(new ClaudeCodeStateProfile(), new ClaudeCodeSettings());

        var launch = adapter.BuildLaunch(seat, LaunchMode.Fresh, new NativeSession(NativeId), SuppliedFiles());

        Assert.Equal(model, launch.Argv[^1]);
        Assert.Equal("--model", launch.Argv[^2]);
        Assert.Equal(originalPermissions, seat.HarnessSettings.Permissions.Allow);
    }

    [Fact]
    public void ValidatesSessionBeforeModeAndRejectsUnsupportedModesWithFixedErrors()
    {
        var adapter = Adapter();
        var badSession = Assert.Throws<InvalidOperationException>(() => adapter.BuildLaunch(
            Seat(), LaunchMode.Fork, new NativeSession("BAD"), SuppliedFiles()));
        Assert.Equal("INVALID_SESSION_ID", badSession.Message);
        Assert.Null(badSession.InnerException);

        AssertLaunchError("LAUNCH_MODE_NOT_SUPPORTED", () => adapter.BuildLaunch(
            Seat(), LaunchMode.Fork, new NativeSession(NativeId), SuppliedFiles()));
        AssertLaunchError("INVALID_LAUNCH", () => adapter.BuildLaunch(
            Seat(), (LaunchMode)99, new NativeSession(NativeId), SuppliedFiles()));
        AssertLaunchError("INVALID_LAUNCH", () => adapter.BuildLaunch(
            Seat(), LaunchMode.Unspecified, new NativeSession(NativeId), SuppliedFiles()));
    }

    [Fact]
    public void RejectsUnsafeSeatPathsIdsAndModels()
    {
        var adapter = Adapter();
        foreach (var seat in new[]
        {
            Seat(harness: "opencode"),
            Seat(seatId: "Upper"),
            Seat(rigId: "x"),
            Seat(seatDir: "/srv/seats/a/../impl"),
            Seat(seatDir: "/srv//impl"),
            Seat(workdir: "relative/path"),
            Seat(workdir: "/srv/seats/impl/space here"),
            Seat(projectionRoot: "/srv/seats/impl/other"),
            Seat(model: "bad\0model"),
            Seat(model: "bad\uD800")
        })
        {
            var exception = Record.Exception(() => adapter.BuildLaunch(
                seat, LaunchMode.Fresh, new NativeSession(NativeId), SuppliedFiles()));
            Assert.True(exception is InvalidOperationException,
                $"Expected INVALID_LAUNCH for seat={seat.Seat}, rig={seat.Rig}, harness={seat.Harness}, " +
                $"seatDir={seat.SeatDir}, workdir={seat.Workdir}, projectionRoot={seat.ProjectionRoot}, model={seat.Model}");
            var invalidLaunch = Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("INVALID_LAUNCH", invalidLaunch.Message);
            Assert.Null(invalidLaunch.InnerException);
        }

        AssertLaunchError("INVALID_SESSION_ID", () => adapter.BuildLaunch(
            Seat(), LaunchMode.Fresh, new NativeSession(LowercaseHexNativeId.ToUpperInvariant()), SuppliedFiles()));
    }

    [Fact]
    public void RejectsMissingDuplicateForeignOrMalformedSuppliedFiles()
    {
        var adapter = Adapter();
        var valid = SuppliedFiles();
        AssertInvalidFiles(valid.Where(file => file.Path != "projection/CLAUDE.md").ToArray());
        AssertValidFiles(valid.Where(file => !file.Path.StartsWith("projection/.claude/skills/", StringComparison.Ordinal)).ToArray());
        AssertInvalidFiles(valid.Append(Clone(valid[0])).ToArray());
        AssertInvalidFiles(valid.Append(new SeatFile
        {
            Root = FileRoot.SeatHome, Path = "projection/settings.json", Content = ByteString.Empty
        }).ToArray());
        AssertInvalidFiles(valid.Select(file =>
        {
            var copy = Clone(file);
            if (copy.Path == "projection/CLAUDE.md") copy.Root = FileRoot.Workspace;
            return copy;
        }).ToArray());
        foreach (var path in new[]
        {
            "projection/.claude/skills/probe/../SKILL.md",
            "projection/.claude/skills//SKILL.md",
            "projection/.claude/skills/probe\\SKILL.md",
            "/projection/.claude/skills/probe/SKILL.md",
            "projection/.claude/skills/probe/\u0001SKILL.md",
            "projection/.claude/skills/probe/\uD800SKILL.md"
        })
        {
            AssertInvalidFiles(valid.Select(file =>
            {
                var copy = Clone(file);
                if (copy.Path == "projection/.claude/skills/probe/SKILL.md") copy.Path = path;
                return copy;
            }).ToArray());
        }

        void AssertInvalidFiles(IReadOnlyList<SeatFile> files) => AssertLaunchError("INVALID_LAUNCH", () =>
            adapter.BuildLaunch(Seat(), LaunchMode.Fresh, new NativeSession(NativeId), files));

        void AssertValidFiles(IReadOnlyList<SeatFile> files) => _ = adapter.BuildLaunch(
            Seat(), LaunchMode.Fresh, new NativeSession(NativeId), files);
    }

    [Fact]
    public void AdapterDeliveryDelegatesToTheOwnedClaudeDelivery()
    {
        var profile = new ClaudeCodeStateProfile();
        var adapter = new ClaudeCodeAdapter(profile, new ClaudeCodeSettings());
        var expected = new ClaudeCodeDelivery(profile).BuildDelivery("operator", "hello\r\nworld", CommandId);

        Assert.Equal(expected, adapter.BuildDelivery("operator", "hello\r\nworld", CommandId));
        Assert.Throws<InvalidOperationException>(() => adapter.BuildDelivery("operator", "/clear", CommandId));
        Assert.Throws<InvalidOperationException>(() => adapter.BuildDelivery("operator", "hello", Guid.Empty));
    }

    private static ClaudeCodeAdapter Adapter() => new(new ClaudeCodeStateProfile(), new ClaudeCodeSettings());

    private static ResolvedSeatParameters Seat(string seatId = "impl", string rigId = "aiakos-dev",
        string harness = "claude-code", string seatDir = "/srv/seats/impl", string workdir = "/srv/seats/impl/repos/app",
        string projectionRoot = "/srv/seats/impl/projection", string? model = null) => new(
        seatId, rigId, "node", harness, model, "subscription", "none", seatDir, workdir, projectionRoot,
        [], new ResolvedHarnessSettings("default", new ResolvedPermissions(["Bash(dotnet build:*)"], [], [])), []);

    private static List<SeatFile> SuppliedFiles() =>
    [
        new() { Root = FileRoot.SeatHome, Path = "projection/CLAUDE.md", Content = ByteString.CopyFromUtf8("guidance\n"), Mode = 0x1FF, Expand = true },
        new() { Root = FileRoot.SeatHome, Path = "projection/.claude/skills/probe/SKILL.md", Content = ByteString.CopyFrom([0, 255, 1]), Mode = 0x1FF, Expand = true },
        new() { Root = FileRoot.SeatHome, Path = "aiakos/bin/aiakos-hook-relay", Content = ByteString.CopyFromUtf8("relay-fixture\n"), Mode = 0x1FF, Expand = true }
    ];

    private static SeatFile Clone(SeatFile file) => new()
    {
        Root = file.Root,
        Path = file.Path,
        Content = ByteString.CopyFrom(file.Content.Span),
        Mode = file.Mode,
        Expand = file.Expand
    };

    private static void AssertLaunchError(string message, Action action)
    {
        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }
}
