using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.Reflection;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class ForwardCompatibilityTests
{
    [Fact]
    public void PreservesUnknownConnectRequestFields()
    {
        byte[] bytes = WriteUnknown(1000, 7);
        ConnectRequest parsed = ConnectRequest.Parser.ParseFrom(bytes);
        Assert.Equal(bytes, parsed.ToByteArray());
    }

    [Fact]
    public void PreservesUnknownEnumValues()
    {
        var output = new CodedOutputStream(new MemoryStream());
        using var stream = new MemoryStream();
        output = new CodedOutputStream(stream);
        output.WriteTag(3, WireFormat.WireType.Varint);
        output.WriteInt32(99);
        output.Flush();
        HarnessEvent parsed = HarnessEvent.Parser.ParseFrom(stream.ToArray());
        Assert.Equal(99, (int)parsed.Kind);
        Assert.False(Enum.IsDefined(parsed.Kind));
    }

    [Fact]
    public void RejectsUnknownCommandAndSeatEventBodiesWithoutDroppingBytes()
    {
        byte[] commandBytes = WriteLengthDelimited(99, [1, 2]);
        Command command = Command.Parser.ParseFrom(commandBytes);
        Assert.Equal(Command.BodyOneofCase.None, command.BodyCase);
        Assert.Equal(ErrorReasons.Unsupported, CommandValidator.Validate(command, Array.Empty<string>())!.Reason);
        Assert.Equal(commandBytes, command.ToByteArray());

        byte[] eventBytes = WriteLengthDelimited(99, [1, 2]);
        SeatEvent seatEvent = SeatEvent.Parser.ParseFrom(eventBytes);
        Assert.Equal(SeatEvent.BodyOneofCase.None, seatEvent.BodyCase);
        Assert.Equal(eventBytes, seatEvent.ToByteArray());
    }

    private static byte[] WriteUnknown(int field, uint value)
    {
        using var stream = new MemoryStream();
        var output = new CodedOutputStream(stream);
        output.WriteTag(field, WireFormat.WireType.Varint);
        output.WriteUInt32(value);
        output.Flush();
        return stream.ToArray();
    }

    private static byte[] WriteLengthDelimited(int field, byte[] value)
    {
        using var stream = new MemoryStream();
        var output = new CodedOutputStream(stream);
        output.WriteTag(field, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(value));
        output.Flush();
        return stream.ToArray();
    }
}
