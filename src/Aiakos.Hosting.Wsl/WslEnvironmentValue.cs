using Aspire.Hosting.ApplicationModel;

namespace Aiakos.Hosting.Wsl;

/// <summary>The prefixes whose variables <see cref="WslResourceBuilderExtensions.WithWslEnvironment"/> forwards.</summary>
public sealed class WslEnvironmentAnnotation : IResourceAnnotation
{
    /// <summary>Environment variable name prefixes forwarded into WSL with <c>/u</c>.</summary>
    public ISet<string> Prefixes { get; } = new SortedSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// The value of <c>WSLENV</c>. It is computed when Aspire resolves the environment, which happens
/// after every environment callback of the resource has run, so it sees variables added by any
/// callback, whatever the order of the calls in <c>Program.cs</c> (spec 0001 R24).
/// </summary>
internal sealed class WslEnvironmentValue(
    IReadOnlyDictionary<string, object> environment,
    IEnumerable<string> prefixes,
    object? existing) : IValueProvider, IManifestExpressionProvider
{
    public const string VariableName = "WSLENV";

    private readonly string[] _prefixes = [.. prefixes];

    public string ValueExpression => Compose(existing as string);

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        var baseValue = existing switch
        {
            null => null,
            string s => s,
            IValueProvider provider => await provider.GetValueAsync(cancellationToken).ConfigureAwait(false),
            _ => existing.ToString(),
        };
        return Compose(baseValue);
    }

    private string Compose(string? baseValue)
    {
        var entries = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(baseValue))
        {
            foreach (var entry in baseValue.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                entries.Add(entry);
                names.Add(entry.Split('/')[0]);
            }
        }

        foreach (var name in environment.Keys.Order(StringComparer.Ordinal))
        {
            if (name != VariableName
                && _prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                && names.Add(name))
            {
                entries.Add(name + "/u");
            }
        }

        return string.Join(':', entries);
    }
}
