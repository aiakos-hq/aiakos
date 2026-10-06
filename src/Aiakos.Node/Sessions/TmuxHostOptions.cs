namespace Aiakos.Node.Sessions;

public sealed class TmuxHostOptions
{
    public string Instance { get; set; } = string.Empty;
    public string Home { get; set; } = string.Empty;
    public string? TmuxPath { get; set; }
    public TimeSpan InvocationTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
