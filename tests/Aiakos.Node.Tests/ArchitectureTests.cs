using System.Reflection;

namespace Aiakos.Node.Tests;

/// <summary>AC16 / R40: the node carries no business logic (ADR 0004).</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] ForbiddenExact = ["Aiakos.Orchestrator", "Aiakos.Data"];
    private static readonly string[] ForbiddenPrefixes = ["Akka", "Npgsql"];

    [Fact]
    public void NodeAssemblyDoesNotReferenceOrchestratorDataAkkaOrNpgsqlDirectly()
    {
        var direct = typeof(NodeOptions).Assembly.GetReferencedAssemblies().Select(static a => a.Name!).ToArray();

        Assert.DoesNotContain(direct, IsForbidden);
        Assert.Contains("Aiakos.ServiceDefaults", direct);
    }

    [Fact]
    public void NodeAssemblyDoesNotReferenceThemTransitively()
    {
        var node = typeof(NodeOptions).Assembly;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.GetName().Name! };
        var pending = new Queue<Assembly>([node]);
        var forbidden = new List<string>();

        while (pending.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name!))
                {
                    continue;
                }

                if (IsForbidden(reference.Name!))
                {
                    forbidden.Add($"{assembly.GetName().Name} -> {reference.Name}");
                    continue;
                }

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (FileNotFoundException)
                {
                    // Not deployed with the tests (e.g. an optional platform assembly); its name was checked above.
                }
            }
        }

        Assert.Empty(forbidden);
        Assert.Contains("Grpc.Net.Client", seen);
    }

    [Fact]
    public void NodeDependencyManifestHasNoForbiddenPackageOrProject()
    {
        // The node's own deps.json lists its whole package and project closure.
        var nodeOutput = Path.GetDirectoryName(typeof(NodeOptions).Assembly.Location)!;
        var deps = Path.Combine(nodeOutput, "aiakos-node.deps.json");
        // The build copies it next to the referenced node executable; a published test layout does not
        // (the two reference walks above still run there), so report a skip rather than pass.
        Assert.SkipUnless(File.Exists(deps), $"{deps} not found (published test layout).");

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(deps));
        var libraries = json.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(static p => p.Name.Split('/')[0])
            .ToArray();

        Assert.Contains("Aiakos.Core", libraries);
        Assert.DoesNotContain(libraries, IsForbidden);
    }

    private static bool IsForbidden(string name) =>
        ForbiddenExact.Any(f => string.Equals(name, f, StringComparison.OrdinalIgnoreCase))
        || ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
