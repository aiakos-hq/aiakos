using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Aiakos.Spec;

public static class RigLoader
{
    private const int MaximumFileBytes = 262144;
    private const string MissingReferenceMessage = "referenced file or directory not found";

    public static LoadResult Load(string rigRoot, string? envPath)
    {
        var diagnostics = new List<Diagnostic>();
        var root = rigRoot ?? "";
        var rigPath = Path.Combine(root, "rig.yaml");
        var rigNode = LoadFile(rigPath, GetDisplayPath(root, rigPath), RigFileKind.Rig, diagnostics, out var rigParsed);
        var rigDocument = new SemanticDocument(rigNode, GetDisplayPath(root, rigPath), rigParsed);

        var agentsByDirectory = new Dictionary<string, SemanticDocument>(PathComparer());
        var unreadableAgents = new HashSet<string>(PathComparer());
        var agentDocuments = new List<SemanticDocument>();
        var seatAgents = new Dictionary<YamlNode, SemanticDocument>();
        if (rigNode is { Kind: YamlNodeKind.Mapping })
        {
            foreach (var seats in Values(rigNode, "seats"))
            {
                if (seats.Kind != YamlNodeKind.Sequence)
                {
                    continue;
                }

                foreach (var seat in seats.Items)
                {
                    if (!IsAgentSeat(seat)) continue;
                    var reference = Values(seat, "agent_ref").FirstOrDefault();
                    if (reference is not { Kind: YamlNodeKind.Scalar, IsNull: false, Value: { } text })
                    {
                        continue;
                    }

                    if (text.StartsWith("path:", StringComparison.Ordinal) || text.StartsWith("git:", StringComparison.Ordinal) ||
                        !text.StartsWith("local:", StringComparison.Ordinal) || text.Length == "local:".Length)
                    {
                        continue;
                    }
                    // Semantic validation emits the single AIK4020 for this reference later.
                    if (SemanticValidator.IsCredentialLike(text)) continue;

                    var source = new ReferenceSource("rig.yaml", Math.Max(1, (int)reference.Mark.Line), Math.Max(1, (int)reference.Mark.Column));
                    var pathDiagnostics = new List<Diagnostic>();
                    var agentDirectory = SharedReferencePaths.Resolve(root, "", text["local:".Length..],
                        SharedReferenceKind.Directory, source, pathDiagnostics);
                    diagnostics.AddRange(pathDiagnostics);
                    if (agentDirectory is null) continue;

                    // One agent directory may back several seats, under different spellings; it is loaded once.
                    if (!agentsByDirectory.TryGetValue(agentDirectory.AbsolutePath, out var agentDocument))
                    {
                        pathDiagnostics.Clear();
                        var agentFile = SharedReferencePaths.Resolve(root, agentDirectory.Path, "agent.yaml",
                            SharedReferenceKind.File, source, pathDiagnostics);
                        diagnostics.AddRange(pathDiagnostics);
                        if (agentFile is null) continue;

                        var agentDisplayPath = agentFile.Path;
                        var agentNode = LoadFile(agentFile.AbsolutePath, agentDisplayPath, RigFileKind.Agent,
                            diagnostics, out var agentParsed, source, out var unreadable);
                        if (unreadable)
                            unreadableAgents.Add(agentDirectory.AbsolutePath);
                        agentDocument = new SemanticDocument(agentNode, agentDisplayPath, agentParsed);
                        agentsByDirectory.Add(agentDirectory.AbsolutePath, agentDocument);
                        agentDocuments.Add(agentDocument);
                    }
                    else if (unreadableAgents.Contains(agentDirectory.AbsolutePath))
                    {
                        diagnostics.Add(new Diagnostic(Severity.Error, "AIK3001", source.File, source.Line,
                            source.Column, MissingReferenceMessage, null));
                    }

                    seatAgents[seat] = agentDocument;
                }
            }
        }

        var actualEnvPath = envPath is null ? Path.Combine(root, "rig.env.yaml") : envPath;
        var envDisplayPath = envPath is null ? GetDisplayPath(root, actualEnvPath) : GetEnvDisplayPath(root, envPath);
        var envNode = LoadFile(actualEnvPath, envDisplayPath, RigFileKind.RigEnv, diagnostics, out var envParsed);
        var envDocument = new SemanticDocument(envNode, envDisplayPath, envParsed);
        var semanticDiagnostics = new SemanticValidator(rigDocument, agentDocuments, seatAgents, envDocument, diagnostics).Validate();
        var environmentDiagnostics = semanticDiagnostics.Where(item => item.File == envDocument.File).ToArray();
        var references = LoadReferenceIntegration.Load(root, rigDocument, agentDocuments, semanticDiagnostics,
            environmentDiagnostics, envDocument.File);
        ResolvedRig? resolved = null;
        IReadOnlyList<Diagnostic> finalDiagnostics = references.Diagnostics;
        if (!references.Diagnostics.Any(item => item.Severity == Severity.Error))
        {
            var assembled = ResolvedRigAssembler.Assemble(rigDocument, agentDocuments, seatAgents, envDocument, references);
            var canonical = RigCanonicalizer.Create(assembled);
            var specHash = canonical.SpecHash;
            var bindingHash = canonical.BindingHash;
            var finalized = assembled with
            {
                Canonical = canonical,
                SpecHash = specHash,
                BindingHash = bindingHash,
                ToolVersion = typeof(RigLoader).Assembly.GetName().Version!.ToString(),
                SeatParameters = assembled.SeatParameters.Select(parameters => parameters with
                {
                    SpecHash = specHash,
                    BindingHash = bindingHash
                }).ToArray()
            };

            var parameters = finalized.SeatParameters.ToArray();
            var projectionDiagnostics = new List<Diagnostic>();
            var projectionFailed = false;
            for (var index = 0; index < parameters.Length; index++)
            {
                var seatParameters = parameters[index];
                var seat = finalized.Seats.First(item => item.Id == seatParameters.Seat && item.Agent is not null);
                var agent = finalized.Agents.First(item => item.Directory == seat.Agent!.AgentDirectory);
                var seatNode = Values(rigNode, "seats").First().Items
                    .First(item => IsAgentSeat(item) && Values(item, "id").FirstOrDefault()?.Value == seatParameters.Seat);
                var idNode = Values(seatNode, "id").First();
                var source = new ReferenceSource("rig.yaml", Math.Max(1, (int)idNode.Mark.Line),
                    Math.Max(1, (int)idNode.Mark.Column));
                var files = new List<EmbeddedFile>
                {
                    new("CLAUDE.md", string.Empty, ClaudeGuidanceRenderer.Render(finalized,
                        seatParameters.Seat, specHash))
                };
                files.AddRange(ClaudeSkillProjection.Map(agent.Skills));
                var checkoutPaths = finalized.Binding.Repos.Select(item => item.Path)
                    .Concat(parameters.Where(item => item.Node == seatParameters.Node)
                        .SelectMany(item => item.Checkouts).Select(item => item.Path))
                    .ToArray();
                var seatDiagnostics = new List<Diagnostic>();
                var projection = ProjectionPlanBuilder.Build(seatParameters.ProjectionRoot, files,
                    checkoutPaths, source, seatDiagnostics);
                projectionDiagnostics.AddRange(seatDiagnostics);
                if (projection is null)
                {
                    projectionFailed = true;
                    continue;
                }

                parameters[index] = seatParameters with { Projection = projection };
            }

            finalDiagnostics = references.Diagnostics.Concat(projectionDiagnostics).ToArray();
            if (!projectionFailed)
                resolved = finalized with { SeatParameters = parameters };
        }
        return new LoadResult(resolved, finalDiagnostics);
    }

    private static YamlNode? LoadFile(string path, string displayPath, RigFileKind kind, List<Diagnostic> allDiagnostics, out bool parsed)
        => LoadFile(path, displayPath, kind, allDiagnostics, out parsed, null, out _);

    private static YamlNode? LoadFile(string path, string displayPath, RigFileKind kind,
        List<Diagnostic> allDiagnostics, out bool parsed, ReferenceSource? readFailureSource, out bool unreadable)
    {
        parsed = false;
        unreadable = false;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }
        catch (IOException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }
        catch (ArgumentException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }
        catch (NotSupportedException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }
        catch (System.Security.SecurityException)
        {
            unreadable = true;
            allDiagnostics.Add(ReadFailureDiagnostic(displayPath, readFailureSource));
            return null;
        }

        if (bytes.Length > MaximumFileBytes)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1004", displayPath, 1, 1, "file is larger than 256 KiB", null));
            return null;
        }

        string text;
        try
        {
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1004", displayPath, 1, 1, "file is not valid UTF-8", null));
            return null;
        }

        var forbiddenDiagnostics = new List<Diagnostic>();
        YamlNode? document;
        Parser? parser = null;
        try
        {
            using var reader = new StringReader(text);
            parser = new Parser(reader);
            var treeParser = new YamlEventTreeParser(parser, text, (code, mark, message) => forbiddenDiagnostics.Add(new Diagnostic(Severity.Error, code, displayPath, Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column), message, null)));
            document = treeParser.Parse();
        }
        catch (YamlException exception)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1002", displayPath, Math.Max(1, (int)exception.Start.Line), Math.Max(1, (int)exception.Start.Column), $"YAML syntax error: {exception.Message}", null));
            return null;
        }
        catch (Exception)
        {
            var mark = parser?.Current?.Start ?? Mark.Empty;
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1002", displayPath, Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column), "YAML syntax error: malformed YAML", null));
            return null;
        }

        parsed = true;

        var validationDiagnostics = new List<Diagnostic>();
        new SchemaValidator(displayPath, kind, validationDiagnostics.Add).Validate(document);
        allDiagnostics.AddRange(forbiddenDiagnostics.Concat(validationDiagnostics).OrderBy(diagnostic => diagnostic.Line).ThenBy(diagnostic => diagnostic.Column));
        return document;
    }

    private static Diagnostic ReadFailureDiagnostic(string displayPath, ReferenceSource? referenceSource) =>
        referenceSource is { } source
            ? new Diagnostic(Severity.Error, "AIK3001", source.File, source.Line, source.Column,
                MissingReferenceMessage, null)
            : new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null);

    private static IEnumerable<YamlNode> Values(YamlNode? mapping, string key)
    {
        if (mapping?.Kind != YamlNodeKind.Mapping)
        {
            yield break;
        }

        var found = false;
        foreach (var entry in mapping.Entries)
        {
            if (!found && entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == key)
            {
                found = true;
                yield return entry.Value;
            }
        }
    }

    private static bool IsAgentSeat(YamlNode seat)
    {
        var kind = Values(seat, "kind").FirstOrDefault();
        if (kind is null) return true;
        if (kind is not { Kind: YamlNodeKind.Scalar, IsNull: false, Value: { } value }) return false;
        return value == "agent";
    }

    private static string GetDisplayPath(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(Path.GetFullPath(root.Length == 0 ? "." : root), Path.GetFullPath(path)).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/');
        }
        catch (NotSupportedException)
        {
            return path.Replace('\\', '/');
        }
        catch (IOException)
        {
            return path.Replace('\\', '/');
        }
    }

    private static string GetEnvDisplayPath(string root, string envPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(Path.GetFullPath(root.Length == 0 ? "." : root), Path.GetFullPath(envPath));
            if (!Path.IsPathRooted(relativePath) && relativePath != ".." && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return relativePath.Replace('\\', '/');
            }
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (IOException)
        {
        }

        return envPath.Replace('\\', '/');
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
