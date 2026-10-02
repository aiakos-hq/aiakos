using Aiakos.Contracts.Node;
using Google.Protobuf;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class PlaceholdersTests
{
    [Fact]
    public void ExpandsOnlyExactTokensInOnePass()
    {
        Assert.Equal("/h/a /w", Placeholders.Expand("${AIAKOS_SEAT_HOME}/a ${AIAKOS_WORKSPACE}", "/h", "/w"));
        Assert.Equal("$AIAKOS_SEAT_HOME ${OTHER} ${aiakos_workspace} $/h", Placeholders.Expand("$AIAKOS_SEAT_HOME ${OTHER} ${aiakos_workspace} $${AIAKOS_SEAT_HOME}", "/h", "/w"));
        Assert.Equal("${AIAKOS_WORKSPACE}", Placeholders.Expand(Placeholders.SeatHome, Placeholders.Workspace, "/w"));
    }

    [Fact]
    public void ExpandsByteStringsWithoutDecodingThem()
    {
        ByteString input = ByteString.CopyFrom(new byte[] { 0xff, (byte)'$', (byte)'{', (byte)'A', (byte)'I', (byte)'A', (byte)'K', (byte)'O', (byte)'S', (byte)'_', (byte)'W', (byte)'O', (byte)'R', (byte)'K', (byte)'S', (byte)'P', (byte)'A', (byte)'C', (byte)'E', (byte)'}', 0xfe });
        Assert.Equal(new byte[] { 0xff, (byte)'/', (byte)'w', 0xfe }, Placeholders.Expand(input, "/h", "/w").ToByteArray());
    }
}
