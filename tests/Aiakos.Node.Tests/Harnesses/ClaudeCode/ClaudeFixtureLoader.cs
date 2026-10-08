using System.Text;

namespace Aiakos.Node.Tests.Harnesses.ClaudeCode;

internal static class ClaudeFixtureLoader
{
    private const string FixtureDirectory = "tests/Aiakos.Node.Tests/Fixtures/claude-code/2.1.284";

    internal static string ReadText(string fileName) => Encoding.UTF8.GetString(ReadBytes(fileName));

    internal static byte[] ReadBytes(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiakos.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate Aiakos.slnx.");
        return File.ReadAllBytes(Path.Combine(directory.FullName, FixtureDirectory, fileName));
    }
}
