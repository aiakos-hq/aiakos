using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aiakos.Spec;

internal static class RigCanonicalizer
{
    internal static CanonicalRigResult Create(ResolvedRig rig)
    {
        ArgumentNullException.ThrowIfNull(rig);

        var sharedJson = Write(SharedTree(rig));
        var bindingJson = Write(BindingTree(rig.Binding));
        var resolvedJson = $"{{\"binding\":{bindingJson},\"shared\":{sharedJson}}}";
        var contents = CollectContents(rig);

        return new CanonicalRigResult(sharedJson, bindingJson, resolvedJson,
            Hash(sharedJson), Hash(bindingJson), contents);
    }

    internal static (string SpecHash, string BindingHash) HashCanonicalJson(JsonElement resolved)
    {
        if (resolved.ValueKind != JsonValueKind.Object)
            throw new FormatException("Resolved JSON is invalid.");

        var properties = resolved.EnumerateObject().ToArray();
        if (properties.Length != 2 || properties.Select(property => property.Name)
                .Order(StringComparer.Ordinal).SequenceEqual(["binding", "shared"]) is false)
            throw new FormatException("Resolved JSON is invalid.");

        var binding = resolved.GetProperty("binding");
        var shared = resolved.GetProperty("shared");
        if (binding.ValueKind != JsonValueKind.Object || shared.ValueKind != JsonValueKind.Object)
            throw new FormatException("Resolved JSON is invalid.");

        var canonical = $"{{\"binding\":{CanonicalJson.Write(binding)},\"shared\":{CanonicalJson.Write(shared)}}}";
        if (!StringComparer.Ordinal.Equals(canonical, resolved.GetRawText()))
            throw new FormatException("Resolved JSON is invalid.");

        return (Hash(CanonicalJson.Write(shared)), Hash(CanonicalJson.Write(binding)));
    }

    private static object SharedTree(ResolvedRig rig) => new
    {
        name = rig.Name,
        description = rig.Description,
        culture = rig.Culture is null ? null : FileTree(rig.Culture),
        repos = Sort(rig.Repos, RepoTree, item => item.Name).Select(RepoTree).ToArray(),
        agents = Sort(rig.Agents, AgentTree, item => item.Directory).Select(AgentTree).ToArray(),
        seats = Sort(rig.Seats, SeatTree, item => item.Id).Select(SeatTree).ToArray()
    };

    private static object BindingTree(ResolvedBinding binding) => new
    {
        seat_root = binding.SeatRoot,
        placement = Sort(binding.Placement, PlacementTree, item => item.Seat).Select(PlacementTree).ToArray(),
        repos = Sort(binding.Repos, RepoBindingTree, item => item.Name).Select(RepoBindingTree).ToArray(),
        secrets = Sort(binding.Secrets, SecretSourceTree, item => item.Name).Select(SecretSourceTree).ToArray()
    };

    private static object RepoTree(ResolvedRepo repo) => new
    {
        name = repo.Name,
        url = repo.Url,
        default_branch = repo.DefaultBranch
    };

    private static object AgentTree(ResolvedAgent agent) => new
    {
        directory = agent.Directory,
        name = agent.Name,
        description = agent.Description,
        default_harness = agent.DefaultHarness,
        default_model = agent.DefaultModel,
        guidance = agent.Guidance.Select(FileTree).ToArray(),
        skills = SortSkills(agent.Skills).Select(SkillTree).ToArray(),
        harness_settings = HarnessSettingsTree(agent.HarnessSettings)
    };

    private static object SeatTree(ResolvedSeat seat) => new
    {
        id = seat.Id,
        kind = seat.Kind,
        description = seat.Description,
        agent = seat.Agent is null ? null : AgentSeatTree(seat.Agent)
    };

    private static object AgentSeatTree(ResolvedAgentSeat seat) => new
    {
        agent_directory = seat.AgentDirectory,
        harness = seat.Harness,
        model = seat.Model,
        checkout = seat.Checkout,
        repos = SortStrings(seat.Repos),
        workdir_repo = seat.WorkdirRepo,
        requires = new
        {
            sandbox = seat.Requires.Sandbox,
            auth = seat.Requires.Auth,
            secrets = SortStrings(seat.Requires.Secrets)
        },
        harness_settings = HarnessSettingsTree(seat.HarnessSettings)
    };

    private static object HarnessSettingsTree(ResolvedHarnessSettings settings) => new
    {
        permission_mode = settings.PermissionMode,
        permissions = new
        {
            allow = settings.Permissions.Allow,
            ask = settings.Permissions.Ask,
            deny = settings.Permissions.Deny
        }
    };

    private static object SkillTree(ResolvedSkill skill) => new
    {
        directory = skill.Directory,
        name = skill.Name,
        description = skill.Description,
        files = Sort(skill.Files, FileTree, item => item.Path).Select(FileTree).ToArray()
    };

    private static object FileTree(EmbeddedFile file) => new
    {
        path = file.Path,
        sha256 = file.Sha256,
        bytes = file.Content.Length
    };

    private static object PlacementTree(ResolvedPlacement placement) => new
    {
        seat = placement.Seat,
        node = placement.Node
    };

    private static object RepoBindingTree(ResolvedRepoBinding repo) => new
    {
        name = repo.Name,
        path = repo.Path
    };

    private static object SecretSourceTree(ResolvedSecretSource secret) => new
    {
        name = secret.Name,
        file = secret.File
    };

    private static T[] Sort<T>(IEnumerable<T> values, Func<T, object> tree, params Func<T, string>[] keys) =>
        values.Select(value => new SortEntry<T>(value,
                keys.Select(key => NormalizeForSort(key(value))).ToArray(), Write(tree(value))))
            .OrderBy(entry => entry.Keys, SortKeyComparer.Instance)
            .ThenBy(entry => entry.Json, StringComparer.Ordinal)
            .Select(entry => entry.Value)
            .ToArray();

    private static ResolvedSkill[] SortSkills(IEnumerable<ResolvedSkill> skills) =>
        Sort(skills, SkillTree, item => item.Name, item => item.Directory);

    private static string[] SortStrings(IEnumerable<string> values) =>
        Sort(values, static value => value, static value => value);

    private static Dictionary<string, byte[]> CollectContents(ResolvedRig rig)
    {
        var files = new List<EmbeddedFile>();
        if (rig.Culture is not null)
            files.Add(rig.Culture);
        foreach (var agent in rig.Agents)
        {
            files.AddRange(agent.Guidance);
            foreach (var skill in agent.Skills)
                files.AddRange(skill.Files);
        }

        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in files.GroupBy(item => item.Sha256, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
            contents.Add(file.Key, file.First().Content.ToArray());
        return contents;
    }

    private static string Write(object value) => CanonicalJson.Write(JsonSerializer.SerializeToElement(value));

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeForSort(string value)
    {
        var result = new StringBuilder(value.Length);
        var segmentStart = 0;
        for (var index = 0; index < value.Length;)
        {
            var scalarLength = char.IsHighSurrogate(value[index]) && index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]) ? 2 : 1;
            var scalar = value.Substring(index, scalarLength);
            try
            {
                _ = scalar.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                result.Append(value.AsSpan(segmentStart, index - segmentStart).ToString()
                    .Normalize(NormalizationForm.FormC));
                result.Append(scalar);
                index += scalarLength;
                segmentStart = index;
                continue;
            }

            index += scalarLength;
        }

        result.Append(value.AsSpan(segmentStart).ToString().Normalize(NormalizationForm.FormC));
        return result.ToString();
    }

    private sealed record SortEntry<T>(T Value, string[] Keys, string Json);

    private sealed class SortKeyComparer : IComparer<string[]>
    {
        public static SortKeyComparer Instance { get; } = new();

        public int Compare(string[]? left, string[]? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
            {
                var comparison = StringComparer.Ordinal.Compare(left[index], right[index]);
                if (comparison != 0)
                    return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }
    }
}
