using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class SemanticTests
{
    private const string MinimalRig = """
        apiVersion: aiakos.dev/v1
        kind: Rig
        name: demo
        workspace:
          repos:
            - name: app
              url: https://github.com/example/app.git
        seats:
          - id: impl
            agent_ref: local:agents/impl
            checkout: shared
        """ + "\n";

    private const string MinimalAgent = """
        apiVersion: aiakos.dev/v1
        kind: Agent
        name: impl
        description: Implements issues
        defaults:
          harness: claude-code
        """ + "\n";

    private const string MinimalEnv = """
        apiVersion: aiakos.dev/v1
        kind: RigEnv
        rig: demo
        placement:
          default_node: local
        repos:
          app:
            path: /home/dev/app
        """ + "\n";

    [Theory]
    [InlineData("path:../agent", "AIK3003", "agent_ref scheme 'path:' in seat 'impl' is not supported in this version (planned for M2)")]
    [InlineData("git:https://example.test/agent", "AIK3003", "agent_ref scheme 'git:' in seat 'impl' is not supported in this version (planned for M2)")]
    [InlineData("agents/impl", "AIK2004", "invalid value 'agents/impl' for field 'agent_ref' in seat 'impl'")]
    public void ValidatesAgentReferenceSemantics(string reference, string code, string message)
    {
        var result = Load(MinimalRig.Replace("local:agents/impl", reference, StringComparison.Ordinal));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(message, diagnostic.Message);
        if (code == "AIK2004") Assert.Equal("must be local:<path>", diagnostic.Hint);
    }

    [Fact]
    public void ReportsMissingHarnessUsingLoadedAgentFile()
    {
        var agent = MinimalAgent.Replace("defaults:\n  harness: claude-code\n", "", StringComparison.Ordinal);
        var result = Load(agent: agent);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("AIK4001", "rig.yaml", 9, 5), (diagnostic.Code, diagnostic.File, diagnostic.Line, diagnostic.Column));
        Assert.Equal("set harness on the seat or defaults.harness in agents/impl/agent.yaml", diagnostic.Hint);
    }

    [Theory]
    [InlineData("opencode", "rig.yaml", 12, 14, "harness 'opencode' in seat 'impl' is not supported in this version (planned for M2)")]
    [InlineData("codex", "agents/impl/agent.yaml", 6, 12, "harness 'codex' in defaults is not supported in this version (planned for M2)")]
    public void ReportsUnsupportedHarnesses(string value, string file, int line, int column, string message)
    {
        var rig = value == "opencode" ? MinimalRig + "    harness: opencode\n" : MinimalRig;
        var agent = value == "codex" ? MinimalAgent.Replace("claude-code", "codex", StringComparison.Ordinal) : MinimalAgent;

        var diagnostic = Assert.Single(Load(rig, agent).Diagnostics);
        Assert.Equal(("AIK4002", file, line, column), (diagnostic.Code, diagnostic.File, diagnostic.Line, diagnostic.Column));
        Assert.Equal(message, diagnostic.Message);
    }

    [Fact]
    public void DetectsDuplicateSeatsAndReposAndReservedSeatIds()
    {
        var rig = MinimalRig.Replace("      url: https://github.com/example/app.git\n", "      url: https://github.com/example/app.git\n    - name: app\n      url: https://github.com/example/second.git\n", StringComparison.Ordinal)
            .Replace("  - id: impl\n", "  - id: node\n", StringComparison.Ordinal)
            + "  - id: node\n    agent_ref: local:agents/impl\n";

        var diagnostics = Load(rig).Diagnostics;
        Assert.Contains(diagnostics, item => item.Code == "AIK4010" && item.Message == "seat id 'node' is reserved" && item.Hint == "reserved ids: all, aiakos, system, orchestrator, node, human");
        Assert.Contains(diagnostics, item => item.Code == "AIK4003" && item.Message == "duplicate repo name 'app'" && item.Hint == "first defined at line 6");
        Assert.Contains(diagnostics, item => item.Code == "AIK4003" && item.Message == "duplicate seat id 'node'" && item.Hint == "first defined at line 11");
    }

    [Fact]
    public void ReportsUnknownSeatReposAndEmptyReposList()
    {
        var unknown = MinimalRig + "    repos: [app, lib]\n";
        var unknownDiagnostic = Assert.Single(Load(unknown).Diagnostics);
        Assert.Equal("AIK4004", unknownDiagnostic.Code);
        Assert.Equal("unknown repo 'lib' in seat 'impl'", unknownDiagnostic.Message);
        Assert.Equal("defined repos: app", unknownDiagnostic.Hint);

        var empty = MinimalRig + "    repos: []\n";
        var emptyDiagnostic = Assert.Single(Load(empty).Diagnostics);
        Assert.Equal("AIK2004", emptyDiagnostic.Code);
        Assert.Equal("field 'repos' in seat 'impl' must have at least one item", emptyDiagnostic.Message);
    }

    [Fact]
    public void ReportsHumanSeatFieldsAndMissingAgentSeat()
    {
        var human = MinimalRig + "  - id: pm\n    kind: human\n    checkout: shared\n";
        var humanDiagnostic = Assert.Single(Load(human).Diagnostics);
        Assert.Equal("AIK4005", humanDiagnostic.Code);
        Assert.Equal("field 'checkout' is not allowed on human seat 'pm'", humanDiagnostic.Message);

        var noAgent = MinimalRig[..MinimalRig.IndexOf("  - id: impl", StringComparison.Ordinal)] + "  - id: pm\n    kind: human\n";
        var noAgentDiagnostic = Assert.Single(Load(noAgent).Diagnostics);
        Assert.Equal(("AIK4006", "rig has no agent seat", 8, 1), (noAgentDiagnostic.Code, noAgentDiagnostic.Message, noAgentDiagnostic.Line, noAgentDiagnostic.Column));
    }

    [Fact]
    public void ValidatesClaudeOwnedSettingsPermissionModeAndRules()
    {
        var agent = MinimalAgent + "harnesses:\n  claude-code:\n    hooks: {}\n    permission_mode: bypassPermissions\n    permissions:\n      allow: [Edit, \"Bash(dotnet test\"]\n";
        var diagnostics = Load(agent: agent).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK4007" && item.Message == "setting 'hooks' in harnesses.claude-code is owned by Aiakos");
        Assert.Contains(diagnostics, item => item.Code == "AIK4008" && item.Hint == "it needs a sandbox (planned for M6)");
        Assert.Contains(diagnostics, item => item.Code == "AIK4009" && item.Message.Contains("allow[1]", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, item => item.Message.Contains("Edit", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsBalancedPermissionSpecifiers()
    {
        var agent = MinimalAgent + "harnesses:\n  claude-code:\n    permissions:\n      allow: [Edit, \"Bash(git log:*)\", mcp__github__get_issue]\n";

        Assert.Empty(Load(agent: agent).Diagnostics);
    }

    [Fact]
    public void ValidatesRepoUrlsAndNeverReportsTheirCredentialLikeUrlAsASecret()
    {
        var credentials = MinimalRig.Replace("https://github.com/example/app.git", "https://bob:hunter2@github.com/example/app.git", StringComparison.Ordinal);
        var diagnostic = Assert.Single(Load(credentials).Diagnostics);
        Assert.Equal("AIK4011", diagnostic.Code);
        Assert.Equal((7, 12), (diagnostic.Line, diagnostic.Column));
        Assert.Equal("repo url in workspace.repos[0] contains credentials", diagnostic.Message);
        Assert.DoesNotContain("hunter2", DiagnosticFormatter.Format(Load(credentials).Diagnostics), StringComparison.Ordinal);

        var unsupported = MinimalRig.Replace("https://github.com/example/app.git", "http://github.com/example/app.git", StringComparison.Ordinal);
        Assert.Contains(Load(unsupported).Diagnostics, item => item.Code == "AIK4011" && item.Hint == "use https://host/path or git@host:org/repo.git");
    }

    [Theory]
    [InlineData("C:\\src\\app", "Windows paths are not valid; node paths are POSIX paths on the node", "AIK5002")]
    [InlineData("$HOME/seats", "'$' expansion is not supported", "AIK5002")]
    [InlineData("src/app", "must be absolute or start with ~/", "AIK5002")]
    [InlineData("/mnt/c/src/app", "/mnt/<drive> paths are slow and file watching is unreliable", "AIK5009")]
    public void ValidatesNodePaths(string path, string hint, string code)
    {
        var env = MinimalEnv.Replace("/home/dev/app", path, StringComparison.Ordinal);

        var diagnostic = Assert.Single(Load(env: env).Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(hint, diagnostic.Hint);
        Assert.Contains($"'{path}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatesCrossFilePlacementRepoAndSecretBindings()
    {
        var rig = MinimalRig + "    requires:\n      secrets: [deploy_key]\n";
        var env = MinimalEnv.Replace("placement:\n  default_node: local\n", "", StringComparison.Ordinal)
            .Replace("repos:\n  app:\n    path: /home/dev/app\n", "", StringComparison.Ordinal);
        var diagnostics = Load(rig, env: env).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK5003" && item.Message == "seat 'impl' has no node" && item.File == "rig.env.yaml");
        Assert.Contains(diagnostics, item => item.Code == "AIK5004" && item.Message == "repo 'app' has no path" && item.Hint == "set repos.app.path");
        Assert.Contains(diagnostics, item => item.Code == "AIK5004" && item.Message == "secret 'deploy_key' has no source" && item.Hint == "set secrets.deploy_key.file");
    }

    [Fact]
    public void WarnsWhenApiKeyAuthOmitsAnthropicSecretAndStillRequiresItsBinding()
    {
        var rig = MinimalRig + "    requires:\n      auth: api-key\n";
        var env = MinimalEnv + "secrets:\n  anthropic_api_key:\n    file: ~/.config/aiakos/secrets/anthropic_api_key\n";

        var diagnostic = Assert.Single(Load(rig, env: env).Diagnostics);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Equal("AIK4012", diagnostic.Code);
        Assert.Equal("seat 'impl' uses auth: api-key but does not list secret 'anthropic_api_key'", diagnostic.Message);
        Assert.Equal("add anthropic_api_key to requires.secrets", diagnostic.Hint);
    }

    [Fact]
    public void ValidatesPlacementKindsAndUnknownRepoBindings()
    {
        var rig = MinimalRig + "  - id: pm\n    kind: human\n    description: Product manager\n";
        var env = MinimalEnv.Replace("  default_node: local\n", "  default_node: local\n  seats:\n    imp: {node: local}\n    pm: {node: local}\n", StringComparison.Ordinal)
            .Replace("    path: /home/dev/app\n", "    path: /home/dev/app\n  lib:\n    path: /home/dev/lib\n", StringComparison.Ordinal);
        var diagnostics = Load(rig, env: env).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK5005" && item.Message == "placement for unknown seat 'imp'" && item.Hint == "agent seats: impl");
        Assert.Contains(diagnostics, item => item.Code == "AIK5005" && item.Message == "placement for human seat 'pm'" && item.Hint == "human seats take no placement");
        Assert.Contains(diagnostics, item => item.Code == "AIK5006" && item.Message == "binding for unknown repo 'lib'" && item.Hint == "defined repos: app");
    }

    [Fact]
    public void ReportsRigNameMismatchAndSecretSourceAtBindingPosition()
    {
        var rig = MinimalRig + "    requires:\n      secrets: [deploy_key]\n";
        var env = MinimalEnv.Replace("rig: demo", "rig: other", StringComparison.Ordinal) + "secrets:\n  deploy_key: {}\n";
        var diagnostics = Load(rig, env: env).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK5001" && item.Message == "rig 'other' does not match rig name 'demo'");
        Assert.Contains(diagnostics, item => item.Code == "AIK5004" && item.Message == "secret 'deploy_key' has no source" && item.Line == 10 && item.Column == 3);
    }

    [Fact]
    public void DetectsInlineAndUnrequiredSecretsAndUnknownBindings()
    {
        var rig = MinimalRig + "    requires:\n      secrets: [deploy_key]\n";
        var env = MinimalEnv.Replace("    path: /home/dev/app\n", "    path: /home/dev/app\n  extra_repo:\n    path: /home/dev/extra\n", StringComparison.Ordinal)
            + "secrets:\n  deploy_key:\n    value: hunter2\n  old_key:\n    file: ~/keys/old\n";
        var diagnostics = Load(rig, env: env).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK5008" && item.Hint == "never inline secrets; use file: with a node-local path");
        Assert.Contains(diagnostics, item => item.Code == "AIK5007" && item.Message == "secret 'old_key' is not required by any seat");
        Assert.Contains(diagnostics, item => item.Code == "AIK5006" && item.Message == "binding for unknown repo 'extra_repo'");
        Assert.DoesNotContain(diagnostics, item => item.Message.Contains("hunter2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sk-ant-example", "Anthropic API key")]
    [InlineData("ghp_example", "GitHub token")]
    [InlineData("github_pat_example", "GitHub token")]
    [InlineData("xoxb-example", "Slack token")]
    [InlineData("xoxp-example", "Slack token")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----", "private key")]
    public void DetectsCredentialLikeScalarsWithoutPrintingTheirValues(string value, string kind)
    {
        var rig = MinimalRig + $"x-note: \"{value}\"\n";
        var formatted = DiagnosticFormatter.Format(Load(rig).Diagnostics);

        Assert.Contains($"credential-like value ({kind})", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain(value, formatted, StringComparison.Ordinal);
        Assert.Contains("never put secrets in rig files; name the secret and bind it in rig.env.yaml", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialScanSuppressesConflictingPathDiagnosticWithoutExposingUrlUserInfo()
    {
        var env = MinimalEnv.Replace("/home/dev/app", "https://bob:hunter2@example.com/x", StringComparison.Ordinal);
        var formatted = DiagnosticFormatter.Format(Load(env: env).Diagnostics);

        Assert.Contains("credential-like value (URL with user info)", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("AIK5002", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("bob", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("xghp_example")]
    [InlineData("rotate the ghp_ token")]
    [InlineData("sk-ant-")]
    [InlineData("https://example.com/a@b")]
    public void DoesNotMatchCredentialLookalikes(string value)
    {
        var result = Load(MinimalRig + $"x-note: \"{value}\"\n");

        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "AIK4020");
    }

    [Fact]
    public void CredentialScanDoesNotTreatMappingKeysAsValues()
    {
        var result = Load(MinimalRig + "x-ghp_example: ordinary value\n");

        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "AIK4020");
    }

    [Fact]
    public void SkipsCrossFileChecksForSyntaxErrorButStillRunsEnvironmentOnlyPathChecks()
    {
        var rig = MinimalRig.Replace("name: demo\n", "name: [demo\n", StringComparison.Ordinal);
        var env = MinimalEnv.Replace("/home/dev/app", "relative", StringComparison.Ordinal);
        var diagnostics = Load(rig, env: env).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK1002" && item.File == "rig.yaml");
        Assert.Contains(diagnostics, item => item.Code == "AIK5002" && item.File == "rig.env.yaml");
        Assert.DoesNotContain(diagnostics, item => item.Code.StartsWith("AIK500", StringComparison.Ordinal) && item.Code != "AIK5002");
    }

    [Fact]
    public void InvalidSeatKindOnlyContributesToAgentSeatExistenceCheck()
    {
        var rig = MinimalRig.Replace("  - id: impl\n", "  - id: impl\n    kind: robot\n", StringComparison.Ordinal);

        var diagnostic = Assert.Single(Load(rig).Diagnostics);
        Assert.Equal("AIK2004", diagnostic.Code);
    }

    [Fact]
    public void InvalidSeatKindIsNotTreatedAsAnAgentForPlacementRules()
    {
        var rig = MinimalRig.Replace("  - id: impl\n", "  - id: impl\n    kind: robot\n", StringComparison.Ordinal);
        var env = MinimalEnv.Replace("  default_node: local\n", "  default_node: local\n  seats:\n    impl: {node: local}\n", StringComparison.Ordinal);

        var diagnostics = Load(rig, env: env).Diagnostics;

        Assert.Contains(diagnostics, item => item.Code == "AIK2004");
        Assert.Contains(diagnostics, item => item.Code == "AIK5005" && item.Message == "placement for unknown seat 'impl'" && item.Hint == "agent seats: ");
        Assert.DoesNotContain(diagnostics, item => item.Code == "AIK5003");
    }

    [Fact]
    public void ReportsOneMissingNodeForEachAgentSeatInSeatOrder()
    {
        var rig = MinimalRig + "  - id: review\n    agent_ref: local:agents/impl\n";
        var env = MinimalEnv.Replace("placement:\n  default_node: local\n", "", StringComparison.Ordinal);
        var diagnostics = Load(rig, env: env).Diagnostics.Where(item => item.Code == "AIK5003").ToArray();

        Assert.Collection(diagnostics,
            item => Assert.Contains("seat 'impl' has no node", item.Message, StringComparison.Ordinal),
            item => Assert.Contains("seat 'review' has no node", item.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void FormatsAndOrdersTheCombinedSemanticDiagnostics()
    {
        var rig = MinimalRig.Replace("  - id: impl\n", "  - id: node\n", StringComparison.Ordinal) + "    repos: [lib]\n";
        var agent = MinimalAgent + "harnesses:\n  claude-code:\n    hooks: {}\n";
        var env = MinimalEnv.Replace("rig: demo", "rig: other", StringComparison.Ordinal)
            .Replace("/home/dev/app", "src/app", StringComparison.Ordinal);

        var formatted = DiagnosticFormatter.Format(Load(rig, agent, env).Diagnostics);

        Assert.Equal(
            "rig.yaml:9:9: error AIK4010: seat id 'node' is reserved\n" +
            "  hint: reserved ids: all, aiakos, system, orchestrator, node, human\n" +
            "rig.yaml:12:13: error AIK4004: unknown repo 'lib' in seat 'node'\n" +
            "  hint: defined repos: app\n" +
            "agents/impl/agent.yaml:9:5: error AIK4007: setting 'hooks' in harnesses.claude-code is owned by Aiakos\n" +
            "rig.env.yaml:3:6: error AIK5001: rig 'other' does not match rig name 'demo'\n" +
            "rig.env.yaml:8:11: error AIK5002: invalid node path 'src/app' for field 'path' in repos.app\n" +
            "  hint: must be absolute or start with ~/\n",
            formatted);
    }

    [Fact]
    public void AcceptsTheCompleteValidConfiguration()
    {
        var rig = """
            apiVersion: aiakos.dev/v1
            kind: Rig
            name: demo
            workspace:
              repos:
                - name: app
                  url: https://github.com/example/app.git
                - name: lib
                  url: git@github.com:example/lib.git
            seats:
              - id: impl
                agent_ref: local:agents/impl
                checkout: shared
              - id: review
                agent_ref: local:agents/impl
                checkout: shared
                repos: [app]
                workdir_repo: app
                requires: {auth: api-key, secrets: [anthropic_api_key]}
              - id: pm
                kind: human
                description: Product manager
            """ + "\n";
        var agent = MinimalAgent + "harnesses:\n  claude-code:\n    permission_mode: acceptEdits\n    permissions:\n      allow: [Edit, \"Bash(dotnet test:*)\", mcp__github__get_issue]\n";
        var env = """
            apiVersion: aiakos.dev/v1
            kind: RigEnv
            rig: demo
            seat_root: ~/aiakos/seats
            placement:
              default_node: local
              seats:
                review:
                  node: local
            repos:
              app: {path: /home/dev/app}
              lib: {path: /home/dev/lib}
            secrets:
              anthropic_api_key: {file: ~/.config/aiakos/secrets/anthropic_api_key}
            """ + "\n";

        Assert.Empty(Load(rig, agent, env).Diagnostics);
    }

    private static LoadResult Load(string? rig = null, string? agent = null, string? env = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-semantic-{Guid.NewGuid():N}");
        var agentDirectory = Path.Combine(root, "agents", "impl");
        Directory.CreateDirectory(agentDirectory);
        try
        {
            File.WriteAllText(Path.Combine(root, "rig.yaml"), rig ?? MinimalRig);
            File.WriteAllText(Path.Combine(agentDirectory, "agent.yaml"), agent ?? MinimalAgent);
            File.WriteAllText(Path.Combine(root, "rig.env.yaml"), env ?? MinimalEnv);
            return RigLoader.Load(root, null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
