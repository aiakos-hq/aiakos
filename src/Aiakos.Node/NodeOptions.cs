using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Aiakos.Node;

/// <summary>
/// Node settings, read from the environment (spec 0001 R39, Design → Configuration → Node
/// environment). Identity comes from here, never from a request body (rule 2).
/// </summary>
public sealed class NodeOptions
{
    public const string OrchestratorUrlVariable = "AIAKOS_ORCHESTRATOR_URL";
    public const string HomeVariable = "AIAKOS_HOME";
    public const string NodeIdVariable = "AIAKOS_NODE_ID";
    public const string NodeTokenVariable = "AIAKOS_NODE_TOKEN";

    /// <summary>Orchestrator gRPC endpoint. Required; there is no default (rule 3).</summary>
    [ConfigurationKeyName(OrchestratorUrlVariable)]
    [Required(ErrorMessage = OrchestratorUrlVariable + " is required (the orchestrator's gRPC endpoint, e.g. http://127.0.0.1:5180).")]
    [AbsoluteHttpUri(ErrorMessage = OrchestratorUrlVariable + " must be an absolute http:// or https:// URI.")]
    public string? OrchestratorUrl { get; set; }

    /// <summary>Node home: absolute, or relative to <c>$HOME</c>; a leading <c>~/</c> is expanded.</summary>
    [ConfigurationKeyName(HomeVariable)]
    [Required(ErrorMessage = HomeVariable + " is required (absolute, or relative to $HOME).")]
    public string? Home { get; set; }

    /// <summary>Node identity.</summary>
    [ConfigurationKeyName(NodeIdVariable)]
    [Required(ErrorMessage = NodeIdVariable + " is required.")]
    public string? NodeId { get; set; }

    /// <summary>Per-node token. Transported only; validated from spec 0002 on.</summary>
    [ConfigurationKeyName(NodeTokenVariable)]
    public string? NodeToken { get; set; }

    /// <summary>The orchestrator URL as a URI. Valid only after validation.</summary>
    public Uri OrchestratorUri => new(OrchestratorUrl!, UriKind.Absolute);

    /// <summary>
    /// Resolves <paramref name="home"/> against <paramref name="userHome"/>: an absolute path is
    /// kept, <c>~</c>, <c>~/x</c> and a relative <c>x</c> resolve under <paramref name="userHome"/>.
    /// Returns <see langword="null"/> when a relative path cannot be resolved because the user home
    /// is unknown (no silent fallback, rule 3).
    /// </summary>
    public static string? ResolveHome(string home, string? userHome)
    {
        ArgumentNullException.ThrowIfNull(home);

        if (home == "~")
        {
            home = string.Empty;
        }
        else if (home.StartsWith("~/", StringComparison.Ordinal))
        {
            home = home[2..];
        }
        else if (Path.IsPathFullyQualified(home))
        {
            return Path.GetFullPath(home);
        }

        if (string.IsNullOrWhiteSpace(userHome))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(userHome, home));
    }
}

/// <summary>Accepts an absolute <c>http</c> or <c>https</c> URI; <see langword="null"/> is left to <see cref="RequiredAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsoluteHttpUriAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}

/// <summary>Source-generated, reflection-free validation of <see cref="NodeOptions"/>.</summary>
[OptionsValidator]
public sealed partial class NodeOptionsValidator : IValidateOptions<NodeOptions>;
