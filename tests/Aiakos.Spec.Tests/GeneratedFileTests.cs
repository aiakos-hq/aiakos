using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class GeneratedFileTests
{
    [Fact]
    public void ReportsOversizedFile()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "rig.yaml"), new byte[262145]);
            File.WriteAllText(Path.Combine(root, "rig.env.yaml"), "apiVersion: aiakos.dev/v1\nkind: RigEnv\nrig: demo\n");

            var diagnostic = Assert.Single(RigLoader.Load(root, null).Diagnostics);
            Assert.Equal("AIK1004", diagnostic.Code);
            Assert.Equal("rig.yaml", diagnostic.File);
            Assert.Equal((1, 1), (diagnostic.Line, diagnostic.Column));
            Assert.Equal("file is larger than 256 KiB", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsInvalidUtf8()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "rig.yaml"), [0xC3, 0x28]);
            File.WriteAllText(Path.Combine(root, "rig.env.yaml"), "apiVersion: aiakos.dev/v1\nkind: RigEnv\nrig: demo\n");

            var diagnostic = Assert.Single(RigLoader.Load(root, null).Diagnostics);
            Assert.Equal("AIK1004", diagnostic.Code);
            Assert.Equal("rig.yaml", diagnostic.File);
            Assert.Equal((1, 1), (diagnostic.Line, diagnostic.Column));
            Assert.Equal("file is not valid UTF-8", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-generated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
