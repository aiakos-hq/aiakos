using System.Text;

namespace Aiakos.Hosting.Wsl.Tests;

public sealed class WslOutputTests
{
    [Fact]
    public void DecodesUtf16leWithoutBom()
    {
        var bytes = Encoding.Unicode.GetBytes("Ubuntu\r\ndocker-desktop\r\n");

        Assert.Equal("Ubuntu\r\ndocker-desktop\r\n", WslOutput.Decode(bytes));
    }

    [Fact]
    public void DecodesUtf16leWithBom()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("nat")];

        Assert.Equal("nat", WslOutput.Decode(bytes));
    }

    [Fact]
    public void DecodesUtf8()
    {
        Assert.Equal("mirrored\n", WslOutput.Decode(Encoding.UTF8.GetBytes("mirrored\n")));
    }

    [Fact]
    public void DecodesUtf8WithBom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("mirrored")];

        Assert.Equal("mirrored", WslOutput.Decode(bytes));
    }

    [Fact]
    public void StripsStrayNuls()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("mirrored"), 0, (byte)'\n'];

        Assert.Equal("mirrored\n", WslOutput.Decode(bytes));
    }

    [Fact]
    public void DecodesEmptyOutput()
    {
        Assert.Equal(string.Empty, WslOutput.Decode([]));
    }
}
