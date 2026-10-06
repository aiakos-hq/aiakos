namespace Aiakos.Spec;

public sealed record ResolvedRepo(string Name, string Url, string DefaultBranch);
public sealed record ResolvedPermissions(IReadOnlyList<string> Allow, IReadOnlyList<string> Ask,
    IReadOnlyList<string> Deny);
public sealed record ResolvedHarnessSettings(string PermissionMode, ResolvedPermissions Permissions);
public sealed record ResolvedRequirements(string Sandbox, string Auth, IReadOnlyList<string> Secrets);
public sealed record ResolvedAgent(string Directory, string Name, string Description,
    string? DefaultHarness, string? DefaultModel, IReadOnlyList<EmbeddedFile> Guidance,
    IReadOnlyList<ResolvedSkill> Skills, ResolvedHarnessSettings HarnessSettings);
public sealed record ResolvedAgentSeat(string AgentDirectory, string Harness, string? Model,
    string Checkout, IReadOnlyList<string> Repos, string WorkdirRepo,
    ResolvedRequirements Requires, ResolvedHarnessSettings HarnessSettings);
public sealed record ResolvedSeat(string Id, string Kind, string Description, ResolvedAgentSeat? Agent);
public sealed record ResolvedPlacement(string Seat, string Node);
public sealed record ResolvedRepoBinding(string Name, string Path);
public sealed record ResolvedSecretSource(string Name, string File);
public sealed record ResolvedBinding(string SeatRoot, IReadOnlyList<ResolvedPlacement> Placement,
    IReadOnlyList<ResolvedRepoBinding> Repos, IReadOnlyList<ResolvedSecretSource> Secrets);
public sealed record ResolvedCheckout(string Repo, string Policy, string Url, string SourcePath,
    string Path, string? Branch, string? BaseRef);
public sealed record ResolvedSeatSecret(string Name, string File, string DeliverAs);
public sealed record ResolvedSeatParameters(string Seat, string Rig, string Node, string Harness,
    string? Model, string Auth, string Sandbox, string SeatDir, string Workdir,
    string ProjectionRoot, IReadOnlyList<ResolvedCheckout> Checkouts,
    ResolvedHarnessSettings HarnessSettings, IReadOnlyList<ResolvedSeatSecret> Secrets)
{
    public string? SpecHash { get; init; }
    public string? BindingHash { get; init; }
}
public sealed record ResolvedRig(string Name, string Description, EmbeddedFile? Culture,
    IReadOnlyList<ResolvedRepo> Repos, IReadOnlyList<ResolvedAgent> Agents,
    IReadOnlyList<ResolvedSeat> Seats, ResolvedBinding Binding,
    IReadOnlyList<ResolvedSeatParameters> SeatParameters)
{
    public string? SpecHash { get; init; }
    public string? BindingHash { get; init; }
    public string? ToolVersion { get; init; }
    public CanonicalRigResult? Canonical { get; init; }
}

public sealed record CanonicalRigResult(string SharedJson, string BindingJson, string ResolvedJson,
    string SpecHash, string BindingHash, IReadOnlyDictionary<string, byte[]> Contents);
