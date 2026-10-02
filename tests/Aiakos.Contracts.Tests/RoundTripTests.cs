using Aiakos.Contracts.Node.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class RoundTripTests
{
    [Fact]
    public void EveryMessageTypeRoundTripsAnEmptyInstance()
    {
        foreach (MessageDescriptor descriptor in NodeLinkReflection.Descriptor.MessageTypes)
        {
            IMessage message = descriptor.Parser.ParseFrom(Array.Empty<byte>());
            IMessage parsed = descriptor.Parser.ParseFrom(message.ToByteArray());
            Assert.Equal(message, parsed);
        }
    }
}
