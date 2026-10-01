using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace Aiakos.Spec;

internal sealed record SemanticDocument(YamlNode? Root, string File, RigFileKind Kind, bool Parsed, string? AgentReference = null);

internal sealed class SemanticValidator
{
    private const string CredentialHint = "never put secrets in rig files; name the secret and bind it in rig.env.yaml";
    private const string ReservedSeatIds = "reserved ids: all, aiakos, system, orchestrator, node, human";
    private static readonly HashSet<string> ReservedSeats = new(["all", "aiakos", "system", "orchestrator", "node", "human"], StringComparer.Ordinal);
    private static readonly Regex PermissionRule = new("^([A-Za-z][A-Za-z0-9_]*|mcp__[A-Za-z0-9_.-]+)(\\(.+\\))?$", RegexOptions.CultureInvariant);
    private static readonly Regex WindowsMount = new("^/mnt/[a-z](/|$)", RegexOptions.CultureInvariant);
    private static readonly (string Kind, Regex Pattern)[] Credentials =
    [
        ("Anthropic API key", new Regex("(?<![A-Za-z0-9])sk-ant-[A-Za-z0-9_-]{6,}", RegexOptions.CultureInvariant)),
        ("GitHub token", new Regex("(?<![A-Za-z0-9])(ghp_|github_pat_)[A-Za-z0-9_]{6,}", RegexOptions.CultureInvariant)),
        ("Slack token", new Regex("(?<![A-Za-z0-9])xox[bp]-[A-Za-z0-9-]{6,}", RegexOptions.CultureInvariant)),
        ("private key", new Regex("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)),
        ("URL with user info", new Regex("[A-Za-z][A-Za-z0-9+.-]*://[^/\\s@]+@", RegexOptions.CultureInvariant))
    ];

    private readonly SemanticDocument rig;
    private readonly IReadOnlyList<SemanticDocument> agents;
    private readonly SemanticDocument env;
    private readonly IReadOnlyList<Diagnostic> schemaDiagnostics;
    private readonly Action<Diagnostic> add;
    private readonly List<SeatInfo> seats = [];
    private readonly List<RepoInfo> repos = [];
    private readonly HashSet<YamlNode> repoUrls = [];

    public SemanticValidator(SemanticDocument rig, IReadOnlyList<SemanticDocument> agents, SemanticDocument env,
        IReadOnlyList<Diagnostic> schemaDiagnostics, Action<Diagnostic> add)
    {
        this.rig = rig;
        this.agents = agents;
        this.env = env;
        this.schemaDiagnostics = schemaDiagnostics;
        this.add = add;
    }

    public void Validate()
    {
        if (Usable(rig)) ValidateRig();
        foreach (var agent in agents)
        {
            if (Usable(agent)) ValidateAgent(agent);
        }

        if (Usable(env)) ValidateEnvironmentPaths();
        ValidateCredentials();
        ValidateCrossFile();
    }

    internal static bool ContainsCredentialLikeText(string? text) =>
        text is not null && Credentials.Any(credential => credential.Pattern.IsMatch(text));

    private void ValidateRig()
    {
        var root = rig.Root!;
        var repoList = Get(root, "workspace") is { Kind: YamlNodeKind.Mapping } workspace ? Get(workspace, "repos") : null;
        if (repoList is { Kind: YamlNodeKind.Sequence })
        {
            for (var index = 0; index < repoList.Items.Count; index++)
            {
                var item = repoList.Items[index];
                if (item.Kind != YamlNodeKind.Mapping) continue;
                var nameNode = Get(item, "name");
                var name = ReadScalar(rig, nameNode);
                if (name is not null) repos.Add(new RepoInfo(name, nameNode!, item, index));
                var url = Get(item, "url");
                if (url is not null) repoUrls.Add(url);
            }

            ValidateDuplicateNames(repos.Select(repo => (repo.Name, repo.Node)), "repo", "repo name");
            foreach (var repo in repos) ValidateRepoUrl(repo);
        }

        var seatList = Get(root, "seats");
        if (seatList is not { Kind: YamlNodeKind.Sequence }) return;
        foreach (var seatNode in seatList.Items)
        {
            if (seatNode.Kind != YamlNodeKind.Mapping) continue;
            var idNode = Get(seatNode, "id");
            var id = ReadScalar(rig, idNode);
            var kindNode = Get(seatNode, "kind");
            var kind = ReadScalar(rig, kindNode);
            var kindInvalid = kindNode is not null && kind is null;
            var agentSeat = kind is null || kind == "agent" || kindInvalid;
            var info = new SeatInfo(seatNode, idNode, id, kind, agentSeat, kindInvalid);
            seats.Add(info);

            if (!kindInvalid && id is not null && ReservedSeats.Contains(id))
            {
                Report(rig, idNode!, "AIK4010", $"seat id '{id}' is reserved", ReservedSeatIds);
            }

            if (!kindInvalid)
            {
                ValidateAgentReference(info);
                ValidateHarness(info);
                ValidateSeatRepos(info);
                ValidateHumanSeat(info);
                ValidateAuthSecret(info);
            }
        }

        ValidateDuplicateNames(seats.Where(seat => !seat.KindInvalid && seat.Id is not null).Select(seat => (seat.Id!, seat.IdNode!)), "seat", "seat id");
        if (!seats.Any(seat => seat.IsAgentSeat))
        {
            var key = FindKey(root, "seats");
            if (key is not null) Report(rig, key, "AIK4006", "rig has no agent seat");
        }
    }

    private void ValidateAgentReference(SeatInfo seat)
    {
        var node = Get(seat.Node, "agent_ref");
        if (node is null) return;
        var value = ReadScalar(rig, node);
        if (value is null) return;
        if (value.StartsWith("path:", StringComparison.Ordinal) || value.StartsWith("git:", StringComparison.Ordinal))
        {
            var scheme = value[..value.IndexOf(':')];
            Report(rig, node, "AIK3003", $"agent_ref scheme '{scheme}:'{SeatWhere(seat)} is not supported in this version (planned for M2)");
        }
        else if (!value.StartsWith("local:", StringComparison.Ordinal) || value.Length == "local:".Length)
        {
            Report(rig, node, "AIK2004", $"invalid value '{value}' for field 'agent_ref'{SeatWhere(seat)}", "must be local:<path>");
        }
    }

    private void ValidateHarness(SeatInfo seat)
    {
        var harnessNode = Get(seat.Node, "harness");
        var harness = ReadScalar(rig, harnessNode);
        if (harness is not null && harness != "claude-code")
        {
            Report(rig, harnessNode!, "AIK4002", $"harness '{harness}'{SeatWhere(seat)} is not supported in this version (planned for M2)");
        }

        var agent = FindAgent(seat);
        if (agent is null || !Usable(agent)) return;
        var defaults = Get(agent.Root!, "defaults");
        if (defaults is not null && defaults.Kind != YamlNodeKind.Mapping) return;
        var defaultsHarnessNode = Get(defaults, "harness");
        if (harnessNode is null && defaultsHarnessNode is null)
        {
            Report(rig, seat.Node, "AIK4001", $"seat '{seat.Id ?? ""}' has no harness", $"set harness on the seat or defaults.harness in {agent.File}");
        }
    }

    private void ValidateSeatRepos(SeatInfo seat)
    {
        var known = repos.Select(repo => repo.Name).ToHashSet(StringComparer.Ordinal);
        var listNode = Get(seat.Node, "repos");
        var selected = new List<string>();
        if (listNode is { Kind: YamlNodeKind.Sequence })
        {
            foreach (var item in listNode.Items)
            {
                var name = ReadScalar(rig, item);
                if (name is null) continue;
                if (!known.Contains(name))
                {
                    Report(rig, item, "AIK4004", $"unknown repo '{name}'{SeatWhere(seat)}", $"defined repos: {string.Join(", ", repos.Select(repo => repo.Name))}");
                }
                else selected.Add(name);
            }
        }
        else if (listNode is null)
        {
            selected.AddRange(repos.Select(repo => repo.Name));
        }

        var workdirNode = Get(seat.Node, "workdir_repo");
        var workdir = ReadScalar(rig, workdirNode);
        if (workdir is not null && !selected.Contains(workdir, StringComparer.Ordinal))
        {
            Report(rig, workdirNode!, "AIK4004", $"workdir_repo '{workdir}' is not in the repos of seat '{seat.Id ?? ""}'", $"repos of the seat: {string.Join(", ", selected)}");
        }
    }

    private void ValidateHumanSeat(SeatInfo seat)
    {
        if (seat.Kind != "human") return;
        var allowed = new HashSet<string>(["id", "kind", "description"], StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in seat.Node.Entries)
        {
            if (entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value is { } field && reported.Add(field) && !allowed.Contains(field) && IsSchemaField(field))
            {
                Report(rig, entry.Key, "AIK4005", $"field '{field}' is not allowed on human seat '{seat.Id ?? ""}'");
            }
        }
    }

    private void ValidateAuthSecret(SeatInfo seat)
    {
        if (!seat.IsAgentSeat) return;
        var agent = FindAgent(seat);
        if (agent is null || !Usable(agent)) return;
        var seatHarness = Get(seat.Node, "harness");
        if (seatHarness is not null && ReadScalar(rig, seatHarness) is null) return;
        var defaultHarness = Get(Get(agent.Root!, "defaults"), "harness");
        if (seatHarness is null && defaultHarness is not null && ReadScalar(agent, defaultHarness) is null) return;
        var harness = ReadScalar(rig, seatHarness) ?? ReadScalar(agent, defaultHarness);
        if (harness != "claude-code") return;
        var requires = Get(seat.Node, "requires");
        var auth = Get(requires, "auth");
        if (ReadScalar(rig, auth) != "api-key") return;
        var secrets = Get(requires, "secrets");
        var hasAnthropic = secrets is { Kind: YamlNodeKind.Sequence } && secrets.Items.Any(item => ReadScalar(rig, item) == "anthropic_api_key");
        if (!hasAnthropic && auth is not null)
        {
            Report(rig, auth, "AIK4012", $"seat '{seat.Id ?? ""}' uses auth: api-key but does not list secret 'anthropic_api_key'", "add anthropic_api_key to requires.secrets", Severity.Warning);
        }
    }

    private void ValidateAgent(SemanticDocument agent)
    {
        var defaults = Get(agent.Root!, "defaults");
        var harness = Get(defaults, "harness");
        var harnessValue = ReadScalar(agent, harness);
        if (harnessValue is not null && harnessValue != "claude-code")
        {
            Report(agent, harness!, "AIK4002", $"harness '{harnessValue}' in defaults is not supported in this version (planned for M2)");
        }

        var claude = Get(Get(agent.Root!, "harnesses"), "claude-code");
        if (claude is not { Kind: YamlNodeKind.Mapping }) return;
        foreach (var entry in claude.Entries)
        {
            if (entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value is "hooks" or "statusLine" or "apiKeyHelper" or "env")
            {
                Report(agent, entry.Key, "AIK4007", $"setting '{entry.Key.Value}' in harnesses.claude-code is owned by Aiakos");
            }
        }

        var permissionMode = Get(claude, "permission_mode");
        if (ReadScalar(agent, permissionMode) == "bypassPermissions")
        {
            Report(agent, permissionMode!, "AIK4008", "permission_mode 'bypassPermissions' is not supported in this version", "it needs a sandbox (planned for M6)");
        }

        var permissions = Get(claude, "permissions");
        if (permissions is not { Kind: YamlNodeKind.Mapping }) return;
        foreach (var listName in new[] { "allow", "ask", "deny" })
        {
            var list = Get(permissions, listName);
            if (list is not { Kind: YamlNodeKind.Sequence }) continue;
            for (var index = 0; index < list.Items.Count; index++)
            {
                var item = list.Items[index];
                var rule = ReadScalar(agent, item);
                if (rule is null) continue;
                if (!PermissionRule.IsMatch(rule) || !BalancedParentheses(rule))
                {
                    Report(agent, item, "AIK4009", $"malformed permission rule '{rule}' for field '{listName}[{index}]' in harnesses.claude-code.permissions", "expected Tool or Tool(specifier)");
                }
            }
        }
    }

    private void ValidateRepoUrl(RepoInfo repo)
    {
        var url = Get(repo.Mapping, "url");
        var value = ReadScalar(rig, url);
        if (value is null || url is null) return;
        var where = $" in workspace.repos[{repo.Index}]";
        if (value.StartsWith("https://", StringComparison.Ordinal))
        {
            var authorityStart = "https://".Length;
            var slash = value.IndexOf('/', authorityStart);
            var authority = slash < 0 ? value[authorityStart..] : value[authorityStart..slash];
            if (authority.Contains('@'))
            {
                Report(rig, url, "AIK4011", $"repo url{where} contains credentials", "URLs must not contain user info; use a credential helper or SSH");
            }
            else if (authority.Length == 0 || slash < 0 || slash == value.Length - 1)
            {
                Report(rig, url, "AIK4011", $"repo url{where} has an unsupported form", "use https://host/path or git@host:org/repo.git");
            }
        }
        else if (value.StartsWith("git@", StringComparison.Ordinal))
        {
            var colon = value.IndexOf(':', 4);
            if (colon <= 4 || colon == value.Length - 1)
            {
                Report(rig, url, "AIK4011", $"repo url{where} has an unsupported form", "use https://host/path or git@host:org/repo.git");
            }
        }
        else
        {
            Report(rig, url, "AIK4011", $"repo url{where} has an unsupported form", "use https://host/path or git@host:org/repo.git");
        }
    }

    private void ValidateEnvironmentPaths()
    {
        var root = env.Root!;
        ValidateNodePath(Get(root, "seat_root"), "seat_root", "");
        var repoMap = Get(root, "repos");
        if (repoMap is { Kind: YamlNodeKind.Mapping })
        {
            foreach (var entry in repoMap.Entries)
            {
                if (entry.Key.Value is not { } id) continue;
                ValidateNodePath(Get(entry.Value, "path"), "path", $" in repos.{id}");
            }
        }

        var secretMap = Get(root, "secrets");
        if (secretMap is { Kind: YamlNodeKind.Mapping })
        {
            foreach (var entry in secretMap.Entries)
            {
                if (entry.Key.Value is not { } id) continue;
                ValidateNodePath(Get(entry.Value, "file"), "file", $" in secrets.{id}");
                var valueKey = FindKey(entry.Value, "value");
                if (valueKey is not null)
                {
                    Report(env, valueKey, "AIK5008", $"inline secret value in secrets.{id}", "never inline secrets; use file: with a node-local path");
                }
            }
        }
    }

    private void ValidateNodePath(YamlNode? node, string field, string where)
    {
        var path = ReadScalar(env, node);
        if (node is null || path is null) return;
        string? hint = null;
        if (path.Contains('\\', StringComparison.Ordinal) || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
        {
            hint = "Windows paths are not valid; node paths are POSIX paths on the node";
        }
        else if (path.Contains('$', StringComparison.Ordinal))
        {
            hint = "'$' expansion is not supported";
        }
        else if (!path.StartsWith('/') && !path.StartsWith("~/", StringComparison.Ordinal))
        {
            hint = "must be absolute or start with ~/";
        }

        if (hint is not null)
        {
            Report(env, node, "AIK5002", $"invalid node path '{path}' for field '{field}'{where}", hint);
        }
        else if (WindowsMount.IsMatch(path))
        {
            Report(env, node, "AIK5009", $"node path '{path}' for field '{field}'{where} is on the Windows filesystem", "/mnt/<drive> paths are slow and file watching is unreliable", Severity.Warning);
        }
    }

    private void ValidateCredentials()
    {
        ValidateCredentialDocument(rig);
        foreach (var agent in agents) ValidateCredentialDocument(agent);
        ValidateCredentialDocument(env);
    }

    private void ValidateCredentialDocument(SemanticDocument document)
    {
        if (!Usable(document) || document.Root is null) return;
        VisitValues(document.Root, node =>
        {
            if (node.Kind != YamlNodeKind.Scalar || node.Value is null || node.IsNull || repoUrls.Contains(node)) return;
            foreach (var (kind, pattern) in Credentials)
            {
                if (pattern.IsMatch(node.Value))
                {
                    Report(document, node, "AIK4020", $"credential-like value ({kind})", CredentialHint);
                    break;
                }
            }
        });
    }

    private void ValidateCrossFile()
    {
        if (Usable(rig) && Usable(env))
        {
            ValidateRigNameBinding();
            ValidatePlacement();
            ValidateRepoBindings();
            ValidateSecretBindings();
            ValidateSecretExtras();
        }

        if (Usable(rig) && Usable(env)) ValidateUnknownRepoBindings();
    }

    private void ValidateRigNameBinding()
    {
        var rigName = ReadScalar(rig, Get(rig.Root!, "name"));
        var envRig = Get(env.Root!, "rig");
        var envName = ReadScalar(env, envRig);
        if (rigName is not null && envName is not null && rigName != envName)
        {
            Report(env, envRig!, "AIK5001", $"rig '{envName}' does not match rig name '{rigName}'");
        }
    }

    private void ValidatePlacement()
    {
        var placement = Get(env.Root!, "placement");
        var placementDamaged = placement is not null && Damaged(env, placement);
        if (placement is not null && placementDamaged) return;
        var defaultNode = ReadScalar(env, Get(placement, "default_node"));
        var placementSeats = Get(placement, "seats");
        var placementById = placementSeats is { Kind: YamlNodeKind.Mapping }
            ? placementSeats.Entries.Where(entry => entry.Key.Value is not null).ToDictionary(entry => entry.Key.Value!, entry => entry, StringComparer.Ordinal)
            : new Dictionary<string, YamlEntry>(StringComparer.Ordinal);

        foreach (var seat in seats.Where(item => item.Kind == "agent" || item.Kind is null))
        {
            if (seat.KindInvalid || seat.Id is null) continue;
            var hasSeatNode = placementById.TryGetValue(seat.Id, out var seatPlacement) && ReadScalar(env, Get(seatPlacement.Value, "node")) is not null;
            if (defaultNode is null && !hasSeatNode)
            {
                var node = FindKey(env.Root!, "placement");
                Report(env, node?.Mark ?? Mark.Empty, node, "AIK5003", $"seat '{seat.Id}' has no node", $"set placement.default_node or placement.seats.{seat.Id}.node");
            }
        }

        foreach (var entry in placementById.Values)
        {
            var id = entry.Key.Value!;
            var matching = seats.FirstOrDefault(seat => seat.Id == id && !seat.KindInvalid);
            if (matching is null || matching.Kind is not null && matching.Kind != "agent")
            {
                var human = matching?.Kind == "human";
                Report(env, entry.Key, "AIK5005", human ? $"placement for human seat '{id}'" : $"placement for unknown seat '{id}'",
                    human ? "human seats take no placement" : $"agent seats: {string.Join(", ", seats.Where(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid).Select(seat => seat.Id).Where(id => id is not null))}");
            }
        }
    }

    private void ValidateRepoBindings()
    {
        var definedRepoList = Get(Get(rig.Root!, "workspace"), "repos");
        if (definedRepoList is not { Kind: YamlNodeKind.Sequence }) return;
        if (seats.Any(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid &&
                              Get(seat.Node, "repos") is { } list && Damaged(rig, list))) return;
        var repoMap = Get(env.Root!, "repos");
        var reposKey = FindKey(env.Root!, "repos");
        var damaged = repoMap is not null && Damaged(env, repoMap);
        if (damaged) return;
        var bound = repoMap is { Kind: YamlNodeKind.Mapping }
            ? repoMap.Entries.Where(entry => entry.Key.Value is not null).ToDictionary(entry => entry.Key.Value!, entry => entry, StringComparer.Ordinal)
            : new Dictionary<string, YamlEntry>(StringComparer.Ordinal);
        foreach (var name in seats.Where(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid)
                     .SelectMany(GetSelectedKnownRepos).Distinct(StringComparer.Ordinal))
        {
            if (bound.TryGetValue(name, out var entry) && Get(entry.Value, "path") is not null) continue;
            var position = bound.TryGetValue(name, out entry) ? entry.Key : reposKey;
            Report(env, position?.Mark ?? Mark.Empty, position, "AIK5004", $"repo '{name}' has no path", $"set repos.{name}.path");
        }
    }

    private void ValidateSecretBindings()
    {
        if (seats.Any(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid &&
                              Get(seat.Node, "requires") is { } requires && Damaged(rig, requires))) return;
        var secretMap = Get(env.Root!, "secrets");
        var secretsKey = FindKey(env.Root!, "secrets");
        if (secretMap is not null && Damaged(env, secretMap)) return;
        var required = RequiredSecrets();
        var bindings = secretMap is { Kind: YamlNodeKind.Mapping }
            ? secretMap.Entries.Where(entry => entry.Key.Value is not null).ToDictionary(entry => entry.Key.Value!, entry => entry, StringComparer.Ordinal)
            : new Dictionary<string, YamlEntry>(StringComparer.Ordinal);
        foreach (var name in required)
        {
            if (bindings.TryGetValue(name, out var entry))
            {
                if (FindKey(entry.Value, "value") is not null || Get(entry.Value, "file") is not null) continue;
                Report(env, entry.Key, "AIK5004", $"secret '{name}' has no source", $"set secrets.{name}.file");
            }
            else
            {
                Report(env, secretsKey?.Mark ?? Mark.Empty, secretsKey, "AIK5004", $"secret '{name}' has no source", $"set secrets.{name}.file");
            }
        }
    }

    private void ValidateUnknownRepoBindings()
    {
        var definedRepoList = Get(Get(rig.Root!, "workspace"), "repos");
        if (definedRepoList is not { Kind: YamlNodeKind.Sequence }) return;
        var repoMap = Get(env.Root!, "repos");
        if (repoMap is not { Kind: YamlNodeKind.Mapping } || Damaged(env, repoMap) || seats.Any(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid && Get(seat.Node, "repos") is { } list && Damaged(rig, list))) return;
        var defined = repos.Select(repo => repo.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in repoMap.Entries)
        {
            if (entry.Key.Value is { } name && !defined.Contains(name))
            {
                Report(env, entry.Key, "AIK5006", $"binding for unknown repo '{name}'", $"defined repos: {string.Join(", ", repos.Select(repo => repo.Name))}");
            }
        }
    }

    private void ValidateSecretExtras()
    {
        var secretMap = Get(env.Root!, "secrets");
        if (secretMap is not { Kind: YamlNodeKind.Mapping } || Damaged(env, secretMap) || seats.Any(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid && Get(seat.Node, "requires") is { } requires && Damaged(rig, requires))) return;
        var required = RequiredSecrets().ToHashSet(StringComparer.Ordinal);
        foreach (var entry in secretMap.Entries)
        {
            if (entry.Key.Value is { } name && !required.Contains(name))
            {
                Report(env, entry.Key, "AIK5007", $"secret '{name}' is not required by any seat");
            }
        }
    }

    private List<string> RequiredSecrets()
    {
        var required = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seat in seats.Where(seat => (seat.Kind == "agent" || seat.Kind is null) && !seat.KindInvalid))
        {
            var secretList = Get(Get(seat.Node, "requires"), "secrets");
            if (secretList is { Kind: YamlNodeKind.Sequence })
            {
                foreach (var item in secretList.Items)
                {
                    if (ReadScalar(rig, item) is { } name && seen.Add(name)) required.Add(name);
                }
            }

            var agent = FindAgent(seat);
            if (agent is null || !Usable(agent)) continue;
            var harness = ReadScalar(rig, Get(seat.Node, "harness")) ?? ReadScalar(agent, Get(Get(agent.Root!, "defaults"), "harness"));
            if (harness == "claude-code" && ReadScalar(rig, Get(Get(seat.Node, "requires"), "auth")) == "api-key")
            {
                if (seen.Add("anthropic_api_key")) required.Add("anthropic_api_key");
            }
        }

        return required;
    }

    private IEnumerable<string> GetSelectedKnownRepos(SeatInfo seat)
    {
        var known = repos.Select(repo => repo.Name).ToHashSet(StringComparer.Ordinal);
        var list = Get(seat.Node, "repos");
        if (list is not { Kind: YamlNodeKind.Sequence })
        {
            if (list is null) return repos.Select(repo => repo.Name);
            return [];
        }

        return list.Items.Select(item => ReadScalar(rig, item)).Where(name => name is not null && known.Contains(name)).Select(name => name!);
    }

    private void ValidateDuplicateNames(IEnumerable<(string Name, YamlNode Node)> values, string subject, string displayName)
    {
        var first = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var (name, node) in values)
        {
            if (first.TryGetValue(name, out var original))
            {
                Report(rig, node, "AIK4003", $"duplicate {displayName} '{name}'", $"first defined at line {original.Mark.Line}");
            }
            else first.Add(name, node);
        }
    }

    private SemanticDocument? FindAgent(SeatInfo seat)
    {
        var reference = ReadScalar(rig, Get(seat.Node, "agent_ref"));
        return reference is null ? null : agents.FirstOrDefault(agent => agent.AgentReference == reference);
    }

    private static bool IsSchemaField(string name) =>
        new[] { "id", "kind", "description", "agent_ref", "harness", "model", "checkout", "repos", "workdir_repo", "requires", "profile", "uses", "startup", "pod" }.Contains(name, StringComparer.Ordinal);

    private string? ReadScalar(SemanticDocument document, YamlNode? node)
    {
        if (node is not { Kind: YamlNodeKind.Scalar, IsNull: false, IsTaggedOrAlias: false, Value: { } value }) return null;
        if (HasDiagnosticAt(document.File, node.Mark, "AIK2001") || HasDiagnosticAt(document.File, node.Mark, "AIK2004")) return null;
        return value;
    }

    private bool Usable(SemanticDocument document) => document.Parsed && !schemaDiagnostics.Any(diagnostic => diagnostic.File == document.File && diagnostic.Code == "AIK2001");

    private bool Damaged(SemanticDocument document, YamlNode container)
    {
        var positions = new HashSet<(int Line, int Column)>();
        Visit(container, node => positions.Add(Position(node.Mark)));
        return schemaDiagnostics.Any(diagnostic => diagnostic.File == document.File && positions.Contains((diagnostic.Line, diagnostic.Column)));
    }

    private bool HasDiagnosticAt(string file, Mark mark, string code) =>
        schemaDiagnostics.Any(diagnostic => diagnostic.File == file && diagnostic.Code == code && (diagnostic.Line, diagnostic.Column) == Position(mark));

    private void Report(SemanticDocument document, YamlNode node, string code, string message, string? hint = null, Severity severity = Severity.Error) =>
        add(new Diagnostic(severity, code, document.File, Math.Max(1, (int)node.Mark.Line), Math.Max(1, (int)node.Mark.Column), message, hint));

    private void Report(SemanticDocument document, Mark mark, YamlNode? source, string code, string message, string? hint = null, Severity severity = Severity.Error) =>
        add(new Diagnostic(severity, code, document.File, Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column), message, hint));

    private static string SeatWhere(SeatInfo seat) => seat.Id is null ? " in seats" : $" in seat '{seat.Id}'";
    private static (int Line, int Column) Position(Mark mark) => (Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column));
    private static YamlNode? Get(YamlNode? mapping, string key) => mapping?.Kind == YamlNodeKind.Mapping ? mapping.Entries.FirstOrDefault(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == key)?.Value : null;
    private static YamlNode? FindKey(YamlNode? mapping, string key) => mapping?.Kind == YamlNodeKind.Mapping ? mapping.Entries.FirstOrDefault(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == key)?.Key : null;

    private static bool BalancedParentheses(string value)
    {
        var depth = 0;
        foreach (var character in value)
        {
            if (character == '(') depth++;
            if (character == ')' && --depth < 0) return false;
        }

        return depth == 0;
    }

    private static void Visit(YamlNode node, Action<YamlNode> action)
    {
        action(node);
        if (node.Kind == YamlNodeKind.Mapping)
        {
            foreach (var entry in node.Entries)
            {
                action(entry.Key);
                Visit(entry.Value, action);
            }
        }
        else if (node.Kind == YamlNodeKind.Sequence)
        {
            foreach (var item in node.Items) Visit(item, action);
        }
    }

    private static void VisitValues(YamlNode node, Action<YamlNode> action)
    {
        action(node);
        if (node.Kind == YamlNodeKind.Mapping)
        {
            foreach (var entry in node.Entries) VisitValues(entry.Value, action);
        }
        else if (node.Kind == YamlNodeKind.Sequence)
        {
            foreach (var item in node.Items) VisitValues(item, action);
        }
    }

    private sealed record SeatInfo(YamlNode Node, YamlNode? IdNode, string? Id, string? Kind, bool IsAgentSeat, bool KindInvalid);
    private sealed record RepoInfo(string Name, YamlNode Node, YamlNode Mapping, int Index);
}
