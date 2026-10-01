namespace Aiakos.Spec;

public enum Severity
{
    Error,
    Warning
}

public sealed record Diagnostic(Severity Severity, string Code, string File, int Line, int Column, string Message, string? Hint);

public sealed record ResolvedRig;

public sealed record LoadResult(ResolvedRig? Rig, IReadOnlyList<Diagnostic> Diagnostics);

public static class DiagnosticFormatter
{
    public static string Format(IEnumerable<Diagnostic> diagnostics)
    {
        var output = new System.Text.StringBuilder();
        foreach (var diagnostic in diagnostics)
        {
            output.Append(diagnostic.File)
                .Append(':').Append(diagnostic.Line)
                .Append(':').Append(diagnostic.Column)
                .Append(": ")
                .Append(diagnostic.Severity == Severity.Error ? "error" : "warning")
                .Append(' ').Append(diagnostic.Code)
                .Append(": ").Append(diagnostic.Message)
                .Append('\n');

            if (diagnostic.Hint is not null)
            {
                output.Append("  hint: ").Append(diagnostic.Hint).Append('\n');
            }
        }

        return output.ToString();
    }
}
