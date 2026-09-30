namespace Aiakos.Hosting.Wsl.Tests;

/// <summary>A scripted <see cref="IWslProcessRunner"/>: answers by the joined argument list.</summary>
internal sealed class FakeWslProcessRunner : IWslProcessRunner
{
    private readonly Dictionary<string, Func<WslProcessResult>> _responses = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public FakeWslProcessRunner On(string arguments, int exitCode, string stdout = "", string stderr = "")
    {
        _responses[arguments] = () => new WslProcessResult(exitCode, stdout, stderr);
        return this;
    }

    public FakeWslProcessRunner Throws(string arguments, Exception exception)
    {
        _responses[arguments] = () => throw exception;
        return this;
    }

    public Task<WslProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Assert.Equal("wsl.exe", fileName);
        var key = string.Join(' ', arguments);
        Calls.Add(key);
        return _responses.TryGetValue(key, out var response)
            ? Task.FromResult(response())
            : throw new InvalidOperationException($"Unexpected call: wsl.exe {key}");
    }
}

internal sealed class FakeWslConfigFile(string? content) : IWslConfigFile
{
    public string? ReadAllText() => content;
}
