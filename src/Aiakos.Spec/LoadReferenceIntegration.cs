namespace Aiakos.Spec;

internal sealed record LoadedAgentReferences(
    IReadOnlyList<EmbeddedFile> Guidance,
    IReadOnlyList<ResolvedSkill> Skills);

internal sealed record LoadedReferences(
    EmbeddedFile? Culture,
    IReadOnlyDictionary<string, LoadedAgentReferences> Agents,
    IReadOnlyList<Diagnostic> Diagnostics);

internal static class LoadReferenceIntegration
{
    internal static LoadedReferences Load(string rigRoot, SemanticDocument rig,
        IReadOnlyList<SemanticDocument> agents, IReadOnlyList<Diagnostic> earlierDiagnostics,
        IReadOnlyList<Diagnostic> environmentDiagnostics, string environmentFile)
    {
        var ordered = new List<Diagnostic>();
        var rigDiagnostics = earlierDiagnostics.Where(item => item.File == rig.File).ToArray();
        AddSorted(ordered, rigDiagnostics);

        var markdownCache = new Dictionary<string, ContentCache>(PathComparer());
        var skillCache = new Dictionary<string, SkillCache>(PathComparer());
        var culture = LoadCulture(rigRoot, rig, earlierDiagnostics, markdownCache, ordered);

        var loadedAgents = new Dictionary<string, LoadedAgentReferences>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            AddSorted(ordered, earlierDiagnostics.Where(item => item.File == agent.File));
            if (!IsDocumentUsable(agent, earlierDiagnostics))
            {
                loadedAgents[agent.File] = new LoadedAgentReferences([], []);
                continue;
            }

            var agentReferences = LoadAgent(rigRoot, agent, earlierDiagnostics, markdownCache, skillCache, ordered);
            loadedAgents[agent.File] = agentReferences;
        }

        AddSorted(ordered, environmentDiagnostics);
        var includedFiles = new HashSet<string>(StringComparer.Ordinal) { rig.File, environmentFile };
        foreach (var agent in agents)
            includedFiles.Add(agent.File);
        AddSorted(ordered, earlierDiagnostics.Where(item => !includedFiles.Contains(item.File)));

        return new LoadedReferences(culture, loadedAgents, ordered);
    }

    private static EmbeddedFile? LoadCulture(string rigRoot, SemanticDocument rig,
        IReadOnlyList<Diagnostic> earlierDiagnostics, IDictionary<string, ContentCache> cache,
        List<Diagnostic> ordered)
    {
        if (!IsDocumentUsable(rig, earlierDiagnostics))
            return null;

        var value = Get(rig.Root, "culture_file");
        if (!TryGetReference(value, rig.File, earlierDiagnostics, out var text, out var source))
            return null;

        var pathDiagnostics = new List<Diagnostic>();
        var file = SharedReferencePaths.Resolve(rigRoot, "", text, SharedReferenceKind.File, source, pathDiagnostics);
        ordered.AddRange(pathDiagnostics);
        if (file is null)
            return null;

        if (!cache.TryGetValue(file.AbsolutePath, out var entry))
        {
            var contentDiagnostics = new List<Diagnostic>();
            var content = SharedContentReader.ReadMarkdown(file, contentDiagnostics);
            entry = new ContentCache(content, contentDiagnostics.ToArray());
            cache.Add(file.AbsolutePath, entry);
        }

        if (!entry.DiagnosticsEmitted)
        {
            ordered.AddRange(entry.Diagnostics);
            entry.DiagnosticsEmitted = true;
        }

        return entry.Content;
    }

    private static LoadedAgentReferences LoadAgent(string rigRoot, SemanticDocument agent,
        IReadOnlyList<Diagnostic> earlierDiagnostics, IDictionary<string, ContentCache> markdownCache,
        IDictionary<string, SkillCache> skillCache, List<Diagnostic> ordered)
    {
        var guidanceFiles = new List<EmbeddedFile>();
        var skills = new List<ResolvedSkill>();
        var ownerDirectory = GetOwnerDirectory(agent.File);

        var guidance = Get(agent.Root, "guidance");
        if (guidance is { Kind: YamlNodeKind.Sequence } && !HasDiagnosticWithin(agent, "guidance", earlierDiagnostics))
        {
            foreach (var item in guidance.Items)
            {
                if (!TryGetReference(item, agent.File, earlierDiagnostics, out var text, out var source))
                    continue;

                var pathDiagnostics = new List<Diagnostic>();
                var file = SharedReferencePaths.Resolve(rigRoot, ownerDirectory, text,
                    SharedReferenceKind.File, source, pathDiagnostics);
                ordered.AddRange(pathDiagnostics);
                if (file is null)
                    continue;

                if (!markdownCache.TryGetValue(file.AbsolutePath, out var entry))
                {
                    var contentDiagnostics = new List<Diagnostic>();
                    var content = SharedContentReader.ReadMarkdown(file, contentDiagnostics);
                    entry = new ContentCache(content, contentDiagnostics.ToArray());
                    markdownCache.Add(file.AbsolutePath, entry);
                }

                if (!entry.DiagnosticsEmitted)
                {
                    ordered.AddRange(entry.Diagnostics);
                    entry.DiagnosticsEmitted = true;
                }

                if (entry.Content is not null)
                    guidanceFiles.Add(entry.Content);
            }
        }

        var declarations = Get(agent.Root, "skills");
        if (declarations is { Kind: YamlNodeKind.Sequence } && !HasDiagnosticWithin(agent, "skills", earlierDiagnostics))
        {
            var firstNames = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
            foreach (var item in declarations.Items)
            {
                if (!TryGetReference(item, agent.File, earlierDiagnostics, out var text, out var source))
                    continue;

                var pathDiagnostics = new List<Diagnostic>();
                var directory = SharedReferencePaths.Resolve(rigRoot, ownerDirectory, text,
                    SharedReferenceKind.Directory, source, pathDiagnostics);
                ordered.AddRange(pathDiagnostics);
                if (directory is null)
                    continue;

                if (!skillCache.TryGetValue(directory.AbsolutePath, out var entry))
                {
                    var skillDiagnostics = new List<Diagnostic>();
                    var skill = SharedSkillReader.Read(rigRoot, directory, source, skillDiagnostics);
                    var scalarDiagnostics = skillDiagnostics.Where(item => IsAt(item, source)).ToArray();
                    var fileDiagnostics = skillDiagnostics.Where(item => !IsAt(item, source))
                        .OrderBy(item => item.File, StringComparer.Ordinal)
                        .ThenBy(item => item.Line)
                        .ThenBy(item => item.Column)
                        .ThenBy(item => item.Code, StringComparer.Ordinal)
                        .ToArray();
                    entry = new SkillCache(skill, scalarDiagnostics, fileDiagnostics);
                    skillCache.Add(directory.AbsolutePath, entry);
                }

                if (entry.ScalarDiagnostics.Count > 0)
                {
                    if (entry.FirstScalarDiagnosticEmitted)
                    {
                        foreach (var diagnostic in entry.ScalarDiagnostics)
                            ordered.Add(AtSource(diagnostic, source));
                    }
                    else
                    {
                        ordered.AddRange(entry.ScalarDiagnostics);
                        entry.FirstScalarDiagnosticEmitted = true;
                    }
                }

                if (!entry.FileDiagnosticsEmitted)
                {
                    ordered.AddRange(entry.FileDiagnostics);
                    entry.FileDiagnosticsEmitted = true;
                }

                if (entry.Skill is null)
                    continue;

                if (firstNames.TryGetValue(entry.Skill.Name, out var first))
                {
                    ordered.Add(new Diagnostic(Severity.Error, "AIK4003", source.File, source.Line, source.Column,
                        $"duplicate skill name '{entry.Skill.Name}'", $"first defined at line {Position(first.Mark).Line}"));
                }
                else
                {
                    firstNames.Add(entry.Skill.Name, item);
                }

                skills.Add(entry.Skill);
            }
        }

        return new LoadedAgentReferences(guidanceFiles, skills);
    }

    private static bool TryGetReference(YamlNode? node, string file,
        IReadOnlyList<Diagnostic> earlierDiagnostics, out string value, out ReferenceSource source)
    {
        value = "";
        var position = node is null ? (1, 1) : Position(node.Mark);
        source = new ReferenceSource(file, position.Item1, position.Item2);
        if (node is not { Kind: YamlNodeKind.Scalar, IsNull: false, IsTaggedOrAlias: false, Value: { } text })
            return false;
        var referenceSource = source;
        if (earlierDiagnostics.Any(item => item.Severity == Severity.Error && IsAt(item, referenceSource)))
            return false;

        value = text;
        return true;
    }

    private static bool IsDocumentUsable(SemanticDocument document, IReadOnlyList<Diagnostic> diagnostics) =>
        document.Parsed && !diagnostics.Any(item => item.File == document.File && item.Code == "AIK2001");

    private static bool HasDiagnosticWithin(SemanticDocument document, string field,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        var entry = FindEntry(document.Root, field);
        if (entry is null)
            return false;

        var positions = new HashSet<(int Line, int Column)>();
        Visit(entry.Key, position => { positions.Add(position); });
        Visit(entry.Value, position => { positions.Add(position); });
        return diagnostics.Any(item => item.File == document.File && positions.Contains((item.Line, item.Column)));
    }

    private static void AddSorted(List<Diagnostic> destination, IEnumerable<Diagnostic> diagnostics)
    {
        destination.AddRange(diagnostics.OrderBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Code, StringComparer.Ordinal));
    }

    private static bool IsAt(Diagnostic diagnostic, ReferenceSource source) =>
        diagnostic.File == source.File && diagnostic.Line == source.Line && diagnostic.Column == source.Column;

    private static Diagnostic AtSource(Diagnostic diagnostic, ReferenceSource source) =>
        diagnostic with { File = source.File, Line = source.Line, Column = source.Column };

    private static (int Line, int Column) Position(YamlDotNet.Core.Mark mark) =>
        (Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column));

    private static string GetOwnerDirectory(string file)
    {
        var separator = file.LastIndexOf('/');
        return separator < 0 ? "" : file[..separator];
    }

    private static YamlNode? Get(YamlNode? mapping, string key) => FindEntry(mapping, key)?.Value;

    private static YamlEntry? FindEntry(YamlNode? mapping, string key) =>
        mapping?.Kind == YamlNodeKind.Mapping
            ? mapping.Entries.FirstOrDefault(entry => entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == key)
            : null;

    private static void Visit(YamlNode node, Action<(int Line, int Column)> visit)
    {
        visit(Position(node.Mark));
        foreach (var entry in node.Entries)
        {
            Visit(entry.Key, visit);
            Visit(entry.Value, visit);
        }
        foreach (var item in node.Items)
            Visit(item, visit);
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class ContentCache(EmbeddedFile? content, IReadOnlyList<Diagnostic> diagnostics)
    {
        public EmbeddedFile? Content { get; } = content;
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
        public bool DiagnosticsEmitted { get; set; }
    }

    private sealed class SkillCache(ResolvedSkill? skill, IReadOnlyList<Diagnostic> scalarDiagnostics,
        IReadOnlyList<Diagnostic> fileDiagnostics)
    {
        public ResolvedSkill? Skill { get; } = skill;
        public IReadOnlyList<Diagnostic> ScalarDiagnostics { get; } = scalarDiagnostics;
        public IReadOnlyList<Diagnostic> FileDiagnostics { get; } = fileDiagnostics;
        public bool FirstScalarDiagnosticEmitted { get; set; }
        public bool FileDiagnosticsEmitted { get; set; }
    }
}
