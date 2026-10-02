using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace Aiakos.Spec;

internal sealed class FieldSchema
{
    public FieldSchema(string name, bool required = false, SchemaType type = SchemaType.String)
    {
        Name = name;
        Required = required;
        Type = type;
    }

    public string Name { get; }
    public bool Required { get; }
    public SchemaType Type { get; }
    public string? Pattern { get; init; }
    public string? PatternFieldName { get; init; }
    public string[]? Values { get; init; }
    public IReadOnlyList<FieldSchema>? Fields { get; init; }
    public FieldSchema? Items { get; init; }
    public string? MapKeyPattern { get; init; }
    public string? MapKeyKind { get; init; }
    public bool NonEmpty { get; init; }
}

internal enum SchemaType
{
    String,
    Mapping,
    List,
    AnyMapping
}

internal sealed class SchemaValidator
{
    private static readonly Regex RigId = new("^[a-z][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex SeatId = new("^[a-z][a-z0-9-]{0,23}$", RegexOptions.CultureInvariant);
    private static readonly Regex RepoId = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex AgentId = new("^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex SecretId = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex NodeId = new("^[a-z0-9][a-z0-9.-]{0,62}$", RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<FieldSchema> RigSchema =
    [
        new("apiVersion", true), new("kind", true),
        new("name", true) { Pattern = RigId.ToString(), PatternFieldName = "name" },
        new("description"), new("culture_file"),
        new("workspace", true, SchemaType.Mapping)
        {
            Fields = [new("repos", true, SchemaType.List)
            {
                NonEmpty = true,
                Items = new("repo", true, SchemaType.Mapping)
                {
                    Fields = [new("name", true) { Pattern = RepoId.ToString(), PatternFieldName = "name" }, new("url", true), new("default_branch")]
                }
            }]
        },
        new("seats", true, SchemaType.List)
        {
            Items = new("seat", true, SchemaType.Mapping)
            {
                Fields = [
                    new("id", true) { Pattern = SeatId.ToString(), PatternFieldName = "id" },
                    new("kind") { Values = ["agent", "human"] }, new("description"),
                    new("agent_ref"), new("harness") { Values = ["claude-code"] }, new("model"),
                    new("checkout") { Values = ["shared", "seat-worktree"] },
                    new("repos", type: SchemaType.List) { Items = new("repo", type: SchemaType.String) { Pattern = RepoId.ToString(), PatternFieldName = "repo id" } },
                    new("workdir_repo") { Pattern = RepoId.ToString(), PatternFieldName = "repo id" },
                    new("requires", type: SchemaType.Mapping)
                    {
                        Fields = [
                            new("sandbox") { Values = ["optional", "required"] },
                            new("auth") { Values = ["subscription", "api-key"] },
                            new("secrets", type: SchemaType.List) { Items = new("secret", type: SchemaType.String) { Pattern = SecretId.ToString(), PatternFieldName = "secret id" } }
                        ]
                    }
                ]
            }
        }
    ];

    private static readonly IReadOnlyList<FieldSchema> AgentSchema =
    [
        new("apiVersion", true), new("kind", true),
        new("name", true) { Pattern = AgentId.ToString(), PatternFieldName = "name" },
        new("description", true),
        new("defaults", type: SchemaType.Mapping)
        {
            Fields = [new("harness") { Values = ["claude-code"] }, new("model")]
        },
        new("guidance", type: SchemaType.List) { Items = new("guidance", type: SchemaType.String) },
        new("skills", type: SchemaType.List) { Items = new("skill", type: SchemaType.String) },
        new("harnesses", type: SchemaType.Mapping)
        {
            Fields = [new("claude-code", type: SchemaType.Mapping)
            {
                Fields = [
                    new("permission_mode") { Values = ["default", "acceptEdits", "plan", "auto", "bypassPermissions"] },
                    new("permissions", type: SchemaType.Mapping)
                    {
                        Fields = [new("allow", type: SchemaType.List) { Items = new("allow", type: SchemaType.String) },
                            new("ask", type: SchemaType.List) { Items = new("ask", type: SchemaType.String) },
                            new("deny", type: SchemaType.List) { Items = new("deny", type: SchemaType.String) }]
                    }
                ]
            }]
        }
    ];

    private readonly Action<Diagnostic> addDiagnostic;
    private readonly string file;
    private readonly RigFileKind fileKind;

    public SchemaValidator(string file, RigFileKind fileKind, Action<Diagnostic> addDiagnostic)
    {
        this.file = file;
        this.fileKind = fileKind;
        this.addDiagnostic = addDiagnostic;
    }

    public void Validate(YamlNode? root)
    {
        if (root?.Kind != YamlNodeKind.Mapping)
        {
            Add("AIK2004", root?.Mark ?? Mark.Empty, "file must contain a mapping");
            return;
        }

        var expectedKind = fileKind switch
        {
            RigFileKind.Rig => "Rig",
            RigFileKind.Agent => "Agent",
            _ => "RigEnv"
        };
        var version = FirstValue(root, "apiVersion");
        var kind = FirstValue(root, "kind");
        var envelopeError = false;
        if (version is null)
        {
            Add("AIK2001", root.Mark, "missing apiVersion", "this version supports aiakos.dev/v1");
            envelopeError = true;
        }
        else if (version.Kind != YamlNodeKind.Scalar || version.IsNull || version.Value != "aiakos.dev/v1")
        {
            var value = version.Kind == YamlNodeKind.Scalar ? version.Value : "";
            Add("AIK2001", version.Mark, $"unsupported apiVersion '{value}'", "this version supports aiakos.dev/v1");
            envelopeError = true;
        }

        if (kind is null)
        {
            Add("AIK2001", root.Mark, $"missing kind, expected '{expectedKind}'");
            envelopeError = true;
        }
        else if (kind.Kind != YamlNodeKind.Scalar || kind.IsNull || kind.Value != expectedKind)
        {
            var actual = kind.Kind == YamlNodeKind.Scalar ? $", found '{kind.Value}'" : "";
            Add("AIK2001", kind.Mark, $"expected kind '{expectedKind}'{actual}");
            envelopeError = true;
        }

        if (envelopeError)
        {
            return;
        }

        switch (fileKind)
        {
            case RigFileKind.Rig:
                ValidateMapping(root, RigSchema, "", isSeat: false);
                break;
            case RigFileKind.Agent:
                ValidateMapping(root, AgentSchema, "", isSeat: false);
                break;
            case RigFileKind.RigEnv:
                ValidateEnv(root);
                break;
        }
    }

    private void ValidateEnv(YamlNode root)
    {
        var fields = new List<FieldSchema>
        {
            new("apiVersion", true), new("kind", true), new("rig", true) { Pattern = RigId.ToString(), PatternFieldName = "rig id" },
            new("seat_root"),
            new("placement", type: SchemaType.Mapping)
            {
                Fields = [new("default_node") { Pattern = NodeId.ToString(), PatternFieldName = "node id" },
                    new("seats", type: SchemaType.AnyMapping) { MapKeyPattern = SeatId.ToString(), MapKeyKind = "seat", Items = new("seat", type: SchemaType.Mapping) { Fields = [new("node") { Pattern = NodeId.ToString(), PatternFieldName = "node id" }] } }]
            },
            new("repos", type: SchemaType.AnyMapping)
            {
                MapKeyPattern = RepoId.ToString(), MapKeyKind = "repo",
                Items = new("repo", type: SchemaType.Mapping) { Fields = [new("path")] }
            },
            new("secrets", type: SchemaType.AnyMapping)
            {
                MapKeyPattern = SecretId.ToString(), MapKeyKind = "secret",
                Items = new("secret", type: SchemaType.Mapping) { Fields = [new("file")] }
            }
        };
        ValidateMapping(root, fields, "", isSeat: false);
    }

    private void ValidateMapping(YamlNode mapping, IReadOnlyList<FieldSchema> schema, string path, bool isSeat)
    {
        var uniqueEntries = UniqueEntries(mapping);
        foreach (var entry in uniqueEntries)
        {
            if (entry.Key.Kind != YamlNodeKind.Scalar || entry.Key.Value is not { } name)
            {
                continue;
            }

            if (name.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }

            if (name == "<<")
            {
                continue;
            }

            if (IsDeferredField(path, name))
            {
                continue;
            }

            var field = schema.FirstOrDefault(candidate => candidate.Name == name);
            if (TryGetReservedField(path, name, out var milestone))
            {
                Add("AIK2005", entry.Key.Mark, $"reserved field '{name}'{Context(path, mapping, isSeat)} is not supported in this version (planned for {milestone})");
                continue;
            }

            if (field is null)
            {
                var hint = FindHint(name, schema);
                Add("AIK2002", entry.Key.Mark, $"unknown field '{name}'{Context(path, mapping, isSeat)}", hint is null ? null : $"did you mean '{hint}'?");
                continue;
            }

            if (entry.Value.IsTaggedOrAlias)
            {
                continue;
            }

            if (TryGetReservedValue(path, name, entry.Value, out var reservedValue, out milestone))
            {
                Add("AIK2005", entry.Value.Mark, $"reserved value '{reservedValue}' for field '{name}'{Context(path, mapping, isSeat)} is not supported in this version (planned for {milestone})");
                continue;
            }

            ValidateFieldValue(field, entry.Value, path, name, mapping, isSeat);
        }

        foreach (var field in schema)
        {
            if (field.Required && !uniqueEntries.Any(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == field.Name))
            {
                if (path == "seats" && field.Name == "agent_ref" && IsHumanSeat(mapping))
                {
                    continue;
                }

                Add("AIK2003", mapping.Mark, $"missing required field '{field.Name}'{Context(path, mapping, isSeat)}");
            }
        }
    }

    private void ValidateFieldValue(FieldSchema field, YamlNode value, string parentPath, string fieldName, YamlNode containingMapping, bool isSeat)
    {
        var context = Context(parentPath, containingMapping, isSeat);
        if (field.Type == SchemaType.String)
        {
            if (value.Kind != YamlNodeKind.Scalar || value.IsNull)
            {
                Add("AIK2004", value.Mark, $"field '{fieldName}' must be a string{context}");
                return;
            }

            if (field.Values is not null && !field.Values.Contains(value.Value, StringComparer.Ordinal))
            {
                if (fieldName == "harness" && value.Value is "opencode" or "codex")
                {
                    Add("AIK4002", value.Mark, $"harness '{value.Value}'{context} is not supported in this version (planned for M2)");
                }
                else if (TryGetReservedScalar(fieldName, parentPath, value.Value!, out var milestone))
                {
                    Add("AIK2005", value.Mark, $"reserved value '{value.Value}' for field '{fieldName}'{context} is not supported in this version (planned for {milestone})");
                }
                else
                {
                    Add("AIK2004", value.Mark, $"invalid value '{value.Value}' for field '{fieldName}'{context}", $"allowed values: {string.Join(", ", field.Values)}");
                }

                return;
            }

            if (field.Pattern is not null && !Regex.IsMatch(value.Value!, field.Pattern, RegexOptions.CultureInvariant))
            {
                var pattern = field.Pattern;
                Add("AIK2004", value.Mark, $"invalid value '{value.Value}' for field '{fieldName}'{context}", $"must match {pattern}");
            }

            return;
        }

        if (field.Type is SchemaType.Mapping or SchemaType.AnyMapping)
        {
            if (value.Kind != YamlNodeKind.Mapping)
            {
                Add("AIK2004", value.Mark, $"field '{fieldName}' must be a mapping{context}");
                return;
            }

            var childPath = AppendPath(parentPath, fieldName);
            if (field.Type == SchemaType.AnyMapping)
            {
                ValidateMapItems(value, field, childPath);
            }
            else
            {
                ValidateMapping(value, field.Fields ?? [], childPath, isSeat: false);
            }

            return;
        }

        if (value.Kind != YamlNodeKind.Sequence)
        {
            Add("AIK2004", value.Mark, $"field '{fieldName}' must be a list{context}");
            return;
        }

        if (field.NonEmpty && value.Items.Count == 0)
        {
            Add("AIK2004", value.Mark, "field 'workspace.repos' must have at least one item");
            return;
        }

        if (fieldName == "repos" && parentPath.StartsWith("seats[", StringComparison.Ordinal) && value.Items.Count == 0)
        {
            Add("AIK2004", value.Mark, $"field 'repos'{Context(parentPath, containingMapping, isSeat: true)} must have at least one item");
            return;
        }

        for (var index = 0; index < value.Items.Count; index++)
        {
            var item = value.Items[index];
            if (item.IsTaggedOrAlias)
            {
                continue;
            }

            var itemSchema = field.Items!;
            var itemPath = $"{AppendPath(parentPath, fieldName)}[{index}]";
            if (itemSchema.Type == SchemaType.Mapping)
            {
                if (item.Kind != YamlNodeKind.Mapping)
                {
                    Add("AIK2004", item.Mark, $"field '{fieldName}[{index}]' must be a mapping{Context(parentPath, value, isSeat)}");
                    continue;
                }

                ValidateMapping(item, itemSchema.Fields ?? [], itemPath, isSeat: fieldName == "seats");
                if (fieldName == "seats")
                {
                    ValidateConditionalSeatFields(item, itemSchema.Fields ?? [], itemPath);
                }
            }
            else
            {
                ValidateFieldValue(itemSchema, item, parentPath, $"{fieldName}[{index}]", containingMapping, isSeat);
            }
        }
    }

    private void ValidateConditionalSeatFields(YamlNode seat, IReadOnlyList<FieldSchema> schema, string path)
    {
        var kind = FirstValue(seat, "kind");
        if (kind?.Kind == YamlNodeKind.Scalar && kind.Value == "human")
        {
            return;
        }

        if (UniqueEntries(seat).Any(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == "agent_ref"))
        {
            return;
        }

        Add("AIK2003", seat.Mark, $"missing required field 'agent_ref'{SeatContext(seat, path)}");
    }

    private void ValidateMapItems(YamlNode mapping, FieldSchema field, string path)
    {
        foreach (var entry in UniqueEntries(mapping))
        {
            if (entry.Key.Kind != YamlNodeKind.Scalar || entry.Key.Value is not { } key)
            {
                continue;
            }

            if (key == "<<")
            {
                continue;
            }

            var regex = field.MapKeyPattern!;
            if (!Regex.IsMatch(key, regex, RegexOptions.CultureInvariant))
            {
                Add("AIK2004", entry.Key.Mark, $"invalid key '{key}' in {path}", $"must match {regex}");
            }

            if (!entry.Value.IsTaggedOrAlias)
            {
                var child = field.Items!;
                if (entry.Value.Kind == YamlNodeKind.Mapping && child.Type == SchemaType.Mapping)
                {
                    ValidateMapping(entry.Value, child.Fields ?? [], AppendPath(path, key), isSeat: false);
                }
                else
                {
                    ValidateFieldValue(child, entry.Value, path, key, mapping, isSeat: false);
                }
            }
        }
    }

    private void Add(string code, Mark mark, string message, string? hint = null, string? context = null)
    {
        addDiagnostic(new Diagnostic(Severity.Error, code, file, Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column), message + (context ?? ""), hint));
    }

    private static IEnumerable<YamlEntry> UniqueEntries(YamlNode mapping)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in mapping.Entries)
        {
            if (entry.Key.Kind != YamlNodeKind.Scalar || entry.Key.Value is not { } key || seen.Add(key))
            {
                yield return entry;
            }
        }
    }

    private static YamlNode? FirstValue(YamlNode mapping, string name) =>
        mapping.Entries.FirstOrDefault(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == name)?.Value;

    private static bool IsHumanSeat(YamlNode mapping) => FirstValue(mapping, "kind") is { Kind: YamlNodeKind.Scalar, Value: "human" };

    internal static string? FindHint(string unknown, IReadOnlyList<FieldSchema> schema)
    {
        var nearest = schema.Select((field, index) => new { field.Name, Index = index, Distance = Distance(unknown, field.Name) })
            .Where(item => item.Distance <= 2)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Index)
            .FirstOrDefault();
        return nearest?.Name;
    }

    internal static int Distance(string first, string second)
    {
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        var current = new int[second.Length + 1];
        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }

    private static string? Context(string path, YamlNode mappingOrValue, bool isSeat)
    {
        if (isSeat && path.StartsWith("seats[", StringComparison.Ordinal) && !path.Contains('.', StringComparison.Ordinal))
        {
            return SeatContext(mappingOrValue, path);
        }

        return string.IsNullOrEmpty(path) ? "" : $" in {path}";
    }

    private static string SeatContext(YamlNode seat, string fallbackPath)
    {
        var id = FirstValue(seat, "id");
        if (id is { Kind: YamlNodeKind.Scalar, IsNull: false, Value: { } value } && SeatId.IsMatch(value))
        {
            return $" in seat '{value}'";
        }

        return $" in {fallbackPath}";
    }

    private static string AppendPath(string parent, string field) => string.IsNullOrEmpty(parent) ? field : $"{parent}.{field}";

    private bool TryGetReservedField(string path, string name, out string milestone)
    {
        var isRigRoot = fileKind == RigFileKind.Rig && path.Length == 0;
        var isSeat = path.StartsWith("seats[", StringComparison.Ordinal) && !path.Contains('.', StringComparison.Ordinal);
        var isAgentRoot = fileKind == RigFileKind.Agent && path.Length == 0;
        var isEnvRoot = fileKind == RigFileKind.RigEnv && path.Length == 0;
        milestone = "M2";
        if (isRigRoot && name is "pods" or "edges" or "imports" or "profiles") return true;
        if (isRigRoot && name == "channels") { milestone = "M4"; return true; }
        if (isRigRoot && name == "egress") { milestone = "M6"; return true; }
        if (isSeat && name is "profile" or "uses" or "startup" or "pod") return true;
        if (isAgentRoot && name is "imports" or "resources" or "profiles" or "startup" or "subagents") return true;
        if (isEnvRoot && name == "nodes") { milestone = "M6"; return true; }
        if (isEnvRoot && name == "channels") { milestone = "M4"; return true; }
        if (fileKind == RigFileKind.Agent && path == "harnesses" && name is "opencode" or "codex") return true;
        if (path.StartsWith("secrets.", StringComparison.Ordinal) && name is "store" or "env") { milestone = "M6"; return true; }
        return false;
    }

    private bool IsDeferredField(string path, string name) =>
        fileKind == RigFileKind.Agent && path == "harnesses.claude-code" && name is "hooks" or "statusLine" or "apiKeyHelper" or "env"
        || fileKind == RigFileKind.RigEnv && path.StartsWith("secrets.", StringComparison.Ordinal) && name == "value";

    private static bool TryGetReservedValue(string path, string field, YamlNode value, out string reservedValue, out string milestone)
    {
        reservedValue = value.Value ?? "";
        milestone = "M2";
        if (value.Kind != YamlNodeKind.Scalar || value.IsNull) return false;
        return TryGetReservedScalar(field, path, reservedValue, out milestone);
    }

    private static bool TryGetReservedScalar(string field, string path, string value, out string milestone)
    {
        milestone = "M2";
        if (field == "checkout" && value == "shared-readonly") { milestone = "M6"; return true; }
        if (field == "checkout" && value == "task-worktree") { milestone = "M8"; return true; }
        if (field == "harnesses" && value is "opencode" or "codex") return true;
        return false;
    }
}

internal enum RigFileKind
{
    Rig,
    Agent,
    RigEnv
}
