using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;
using Google.Protobuf;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class HarnessEventLimitsTests
{
    [Fact]
    public void LimitsRawPayloadAndAttributes()
    {
        var eventValue = new HarnessEvent { Raw = ByteString.CopyFrom(new byte[ContractLimits.MaxHarnessRawBytes + 1]) };
        eventValue.Attributes["a"] = new string('a', 1025);
        eventValue.Attributes["e"] = new string('é', 600);
        HarnessEventLimits.Apply(eventValue);
        Assert.Equal((uint)(ContractLimits.MaxHarnessRawBytes + 1), eventValue.RawSize);
        Assert.Equal(ContractLimits.MaxHarnessRawBytes, eventValue.Raw.Length);
        Assert.True(eventValue.RawTruncated);
        Assert.Equal(1024, System.Text.Encoding.UTF8.GetByteCount(eventValue.Attributes["a"]));
        Assert.Equal(512, eventValue.Attributes["e"].Length);
    }
}
