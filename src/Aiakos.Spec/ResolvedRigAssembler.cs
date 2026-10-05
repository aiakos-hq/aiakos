namespace Aiakos.Spec;

internal static class ResolvedRigAssembler
{
    internal static ResolvedRig Assemble(SemanticDocument rig, IReadOnlyList<SemanticDocument> agents,
        IReadOnlyDictionary<YamlNode, SemanticDocument> seatAgents, SemanticDocument env,
        LoadedReferences references)
    {
        var root = rig.Root!;
        var envRoot = env.Root!;
        var rigName = Scalar(Get(root, "name"))!;
        var repos = Sequence(Get(Get(root, "workspace"), "repos"))
            .Select(item => new ResolvedRepo(Scalar(Get(item, "name"))!, Scalar(Get(item, "url"))!,
                Scalar(Get(item, "default_branch")) ?? "main"))
            .ToArray();
        var reposByName = repos.ToDictionary(repo => repo.Name, StringComparer.Ordinal);

        var resolvedAgents = new List<ResolvedAgent>();
        var resolvedAgentsByFile = new Dictionary<string, ResolvedAgent>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            var node = agent.Root!;
            var directory = DirectoryOf(agent.File);
            var defaults = Get(node, "defaults");
            var resolved = new ResolvedAgent(directory, Scalar(Get(node, "name"))!,
                Scalar(Get(node, "description"))!, Scalar(Get(defaults, "harness")), Scalar(Get(defaults, "model")),
                references.Agents.TryGetValue(agent.File, out var loaded) ? loaded.Guidance : [],
                loaded?.Skills ?? [], ReadHarnessSettings(node));
            resolvedAgents.Add(resolved);
            resolvedAgentsByFile.Add(agent.File, resolved);
        }

        var seatNodes = Sequence(Get(root, "seats"));
        var resolvedSeats = new List<ResolvedSeat>();
        var agentSeatNodes = new List<(YamlNode Node, ResolvedAgent Agent, ResolvedAgentSeat Seat)>();
        foreach (var seatNode in seatNodes)
        {
            var id = Scalar(Get(seatNode, "id"))!;
            var kind = Scalar(Get(seatNode, "kind")) ?? "agent";
            var description = Scalar(Get(seatNode, "description")) ?? "";
            if (kind == "human")
            {
                resolvedSeats.Add(new ResolvedSeat(id, kind, description, null));
                continue;
            }

            var agentDocument = seatAgents[seatNode];
            var agent = resolvedAgentsByFile[agentDocument.File];
            var seatHarness = Scalar(Get(seatNode, "harness"));
            var harness = seatHarness ?? agent.DefaultHarness!;
            var model = Get(seatNode, "model") is { } modelNode ? Scalar(modelNode) : agent.DefaultModel;
            var selectedRepos = Get(seatNode, "repos") is { } reposNode
                ? Sequence(reposNode).Select(Scalar).Where(value => value is not null).Select(value => value!).Distinct(StringComparer.Ordinal).ToArray()
                : repos.Select(repo => repo.Name).ToArray();
            var workdirRepo = Scalar(Get(seatNode, "workdir_repo")) ?? selectedRepos.FirstOrDefault() ?? "";
            var requires = Get(seatNode, "requires");
            var auth = Scalar(Get(requires, "auth")) ?? "subscription";
            var sandbox = Scalar(Get(requires, "sandbox")) ?? "optional";
            var requiredSecrets = Get(requires, "secrets") is { } secretsNode
                ? Sequence(secretsNode).Select(Scalar).Where(value => value is not null).Select(value => value!).Distinct(StringComparer.Ordinal).ToList()
                : [];
            if (auth == "api-key" && !requiredSecrets.Contains("anthropic_api_key", StringComparer.Ordinal))
                requiredSecrets.Add("anthropic_api_key");
            var settings = agent.HarnessSettings;
            var agentSeat = new ResolvedAgentSeat(agent.Directory, harness, model,
                Scalar(Get(seatNode, "checkout")) ?? "seat-worktree", selectedRepos, workdirRepo,
                new ResolvedRequirements(sandbox, auth, requiredSecrets), settings);
            resolvedSeats.Add(new ResolvedSeat(id, kind, description.Length == 0 ? agent.Description : description, agentSeat));
            agentSeatNodes.Add((seatNode, agent, agentSeat));
        }

        var seatRoot = Scalar(Get(envRoot, "seat_root")) ?? "~/aiakos/seats";
        var defaultNode = Scalar(Get(Get(envRoot, "placement"), "default_node"));
        var nodeOverrides = Map(Get(Get(envRoot, "placement"), "seats"));
        var placement = agentSeatNodes.Select(pair => new ResolvedPlacement(
            Scalar(Get(pair.Node, "id"))!,
            Scalar(Get(nodeOverrides.GetValueOrDefault(Scalar(Get(pair.Node, "id"))!), "node")) ?? defaultNode!)).ToArray();
        var nodeBySeat = placement.ToDictionary(item => item.Seat, item => item.Node, StringComparer.Ordinal);
        var envRepos = Map(Get(envRoot, "repos"));
        var repoBindings = envRepos.Select(pair => new ResolvedRepoBinding(pair.Key,
            Scalar(Get(pair.Value, "path"))!)).ToArray();
        var repoPaths = repoBindings.ToDictionary(item => item.Name, item => item.Path, StringComparer.Ordinal);
        var envSecrets = Map(Get(envRoot, "secrets"));
        var secretBindings = envSecrets.Select(pair => new ResolvedSecretSource(pair.Key,
            Scalar(Get(pair.Value, "file"))!)).ToArray();
        var secretFiles = secretBindings.ToDictionary(item => item.Name, item => item.File, StringComparer.Ordinal);
        var binding = new ResolvedBinding(seatRoot, placement, repoBindings, secretBindings);

        var parameters = new List<ResolvedSeatParameters>();
        foreach (var resolvedSeat in resolvedSeats.Where(item => item.Agent is not null))
        {
            var seat = resolvedSeat.Agent!;
            var node = nodeBySeat[resolvedSeat.Id];
            var seatDir = JoinNodePath(seatRoot, rigName, resolvedSeat.Id);
            var checkouts = new List<ResolvedCheckout>();
            foreach (var repoName in seat.Repos)
            {
                var repo = reposByName[repoName];
                var sourcePath = repoPaths[repoName];
                var isShared = seat.Checkout == "shared";
                checkouts.Add(new ResolvedCheckout(repoName, seat.Checkout, repo.Url, sourcePath,
                    isShared ? sourcePath : JoinNodePath(seatDir, "repos", repoName),
                    isShared ? null : $"aiakos/{rigName}/{resolvedSeat.Id}",
                    isShared ? null : $"origin/{repo.DefaultBranch}"));
            }

            var secrets = seat.Requires.Secrets.Select(name => new ResolvedSeatSecret(name,
                secretFiles[name], "file")).ToArray();
            var workdir = checkouts.FirstOrDefault(checkout => checkout.Repo == seat.WorkdirRepo)?.Path ?? "";
            parameters.Add(new ResolvedSeatParameters(resolvedSeat.Id, rigName, node, seat.Harness,
                seat.Model, seat.Requires.Auth, seat.Requires.Sandbox, seatDir, workdir,
                JoinNodePath(seatDir, "projection"), checkouts, seat.HarnessSettings, secrets));
        }

        return new ResolvedRig(rigName, Scalar(Get(root, "description")) ?? "",
            references.Culture, repos, resolvedAgents, resolvedSeats, binding, parameters);
    }

    private static ResolvedHarnessSettings ReadHarnessSettings(YamlNode agent)
    {
        var claude = Get(Get(agent, "harnesses"), "claude-code");
        var permissions = Get(claude, "permissions");
        return new ResolvedHarnessSettings(Scalar(Get(claude, "permission_mode")) ?? "default",
            new ResolvedPermissions(ReadStringList(Get(permissions, "allow")), ReadStringList(Get(permissions, "ask")),
                ReadStringList(Get(permissions, "deny"))));
    }

    private static string[] ReadStringList(YamlNode? node) =>
        node is { Kind: YamlNodeKind.Sequence } ? node.Items.Select(Scalar).Where(item => item is not null).Select(item => item!).ToArray() : [];

    private static Dictionary<string, YamlNode> Map(YamlNode? node) => node is { Kind: YamlNodeKind.Mapping }
        ? node.Entries.Where(entry => entry.Key.Value is not null).ToDictionary(entry => entry.Key.Value!, entry => entry.Value, StringComparer.Ordinal)
        : new Dictionary<string, YamlNode>(StringComparer.Ordinal);

    private static List<YamlNode> Sequence(YamlNode? node) =>
        node is { Kind: YamlNodeKind.Sequence } ? node.Items : [];

    private static string? Scalar(YamlNode? node) =>
        node is { Kind: YamlNodeKind.Scalar, IsNull: false, IsTaggedOrAlias: false } ? node.Value : null;

    private static YamlNode? Get(YamlNode? mapping, string key) => mapping?.Kind == YamlNodeKind.Mapping
        ? mapping.Entries.FirstOrDefault(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == key)?.Value
        : null;

    private static string DirectoryOf(string file)
    {
        var index = file.LastIndexOf('/');
        return index < 0 ? "" : file[..index];
    }

    private static string JoinNodePath(string root, params string[] segments)
    {
        var prefix = root.TrimEnd('/');
        var suffix = string.Join('/', segments.Select(segment => segment.Trim('/')));
        if (prefix.Length == 0) return "/" + suffix;
        return prefix + "/" + suffix;
    }
}
