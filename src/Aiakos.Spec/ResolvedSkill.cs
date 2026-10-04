namespace Aiakos.Spec;

public sealed record ResolvedSkill(string Directory, string Name, string Description,
    IReadOnlyList<EmbeddedFile> Files);
