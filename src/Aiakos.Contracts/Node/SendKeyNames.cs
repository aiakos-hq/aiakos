namespace Aiakos.Contracts.Node;

public static class SendKeyNames
{
    public static IReadOnlyList<string> All { get; } =
        ["Enter", "Escape", "Tab", "Up", "Down", "Left", "Right", "C-c", "C-d"];

    public static bool IsAllowed(string key) => All.Contains(key, StringComparer.Ordinal);
}
