namespace Aiakos.Node.Harnesses.ClaudeCode;

public static class ClaudeScreenClassifier
{
    public static string Classify(string capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return "unknown";
    }

    public static string ClassifyExitReason(string capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return "unknown";
    }
}
