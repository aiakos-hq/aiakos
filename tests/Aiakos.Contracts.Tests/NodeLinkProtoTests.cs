using Aiakos.Contracts.Node.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class NodeLinkProtoTests
{
    private static readonly string[] ConnectRequestBody =
        ["hello=10", "heartbeat=11", "command_ack=12", "seat_event=13", "goodbye=14"];

    private static readonly string[] ConnectResponseBody =
        ["welcome=10", "command=11", "event_ack=12", "goodbye=13"];

    private static readonly string[] CommandBody =
        ["start_seat=10", "deliver_input=11", "send_keys=12", "capture_pane=13", "stop_seat=14"];

    [Fact]
    public void IsTheV1PackageInItsVersionedFile()
    {
        Assert.Equal("aiakos.node.v1", NodeLinkReflection.Descriptor.Package);
        Assert.Equal("aiakos/node/v1/node_link.proto", NodeLinkReflection.Descriptor.Name);
    }

    [Fact]
    public void HasExactlyOneBidirectionalRpc()
    {
        ServiceDescriptor service = Assert.Single(NodeLinkReflection.Descriptor.Services);
        Assert.Equal("NodeLinkService", service.Name);

        MethodDescriptor method = Assert.Single(service.Methods);
        Assert.Equal("Connect", method.Name);
        Assert.True(method.IsClientStreaming);
        Assert.True(method.IsServerStreaming);
        Assert.Same(ConnectRequest.Descriptor, method.InputType);
        Assert.Same(ConnectResponse.Descriptor, method.OutputType);
    }

    [Fact]
    public void EveryEnumStartsWithAnUnspecifiedValue()
    {
        Assert.NotEmpty(NodeLinkReflection.Descriptor.EnumTypes);

        foreach (EnumDescriptor type in NodeLinkReflection.Descriptor.EnumTypes)
        {
            EnumValueDescriptor? zero = type.FindValueByNumber(0);
            Assert.True(zero is not null, $"{type.Name} has no value 0");
            Assert.EndsWith("_UNSPECIFIED", zero.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ErrorCodeMirrorsGrpcStatusCodes()
    {
        foreach (ErrorCode code in Enum.GetValues<ErrorCode>())
        {
            if (code == ErrorCode.Unspecified)
            {
                continue;
            }

            Assert.True(Enum.TryParse(code.ToString(), out StatusCode status), $"no StatusCode named {code}");
            Assert.Equal((int)status, (int)code);
        }

        Assert.Equal(3, (int)ErrorCode.InvalidArgument);
        Assert.Equal(14, (int)ErrorCode.Unavailable);
    }

    [Fact]
    public void EnvelopeOneofsKeepTheirFieldNumbers()
    {
        Assert.Equal(ConnectRequestBody, BodyFields(ConnectRequest.Descriptor));
        Assert.Equal(ConnectResponseBody, BodyFields(ConnectResponse.Descriptor));
        Assert.Equal(CommandBody, BodyFields(Command.Descriptor));
    }

    [Fact]
    public void OptionalFieldsDistinguishUnsetFromZero()
    {
        var exited = new ProcessExited();
        Assert.False(exited.HasExitCode);
        Assert.False(exited.HasSignal);

        exited.ExitCode = 0;
        Assert.True(exited.HasExitCode);
        Assert.False(exited.HasSignal);

        var usage = new Usage();
        Assert.False(usage.HasContextUsedPercent);

        usage.ContextUsedPercent = 0;
        Assert.True(usage.HasContextUsedPercent);
    }

    [Fact]
    public void RoundTripsASeatEventWithAHarnessEvent()
    {
        var request = new ConnectRequest
        {
            Trace = new TraceContext
            {
                Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                Tracestate = "vendor=value",
            },
            SeatEvent = new SeatEvent
            {
                SeatId = "0198f3a2-7c11-7d6e-9a55-3f1b2c4d5e6f",
                LaunchId = "0198f3a2-7c12-7b00-8d21-6a7b8c9d0e1f",
                Seq = 7,
                SourceSeq = 42,
                Harness = new HarnessEvent
                {
                    Harness = "claude-code",
                    NativeName = "UserPromptSubmit",
                    Kind = HarnessEventKind.PromptSubmitted,
                    NativeSessionId = "3d0c9a4e-5b1f-4c7a-9e2d-8f6a1b2c3d4e",
                    Attributes = { ["turn_id"] = "prompt-1" },
                    Raw = ByteString.CopyFrom(1, 2, 3),
                    RawContentType = "application/json",
                    RawTruncated = true,
                    RawSize = 300_000,
                    Origin = EventOrigin.Live,
                },
            },
        };

        ConnectRequest parsed = ConnectRequest.Parser.ParseFrom(request.ToByteArray());

        Assert.Equal(request, parsed);
        Assert.Equal(ConnectRequest.BodyOneofCase.SeatEvent, parsed.BodyCase);
        Assert.Equal(SeatEvent.BodyOneofCase.Harness, parsed.SeatEvent.BodyCase);
    }

    [Fact]
    public void GeneratesServerAndClientStubs()
    {
        Assert.True(typeof(NodeLinkService.NodeLinkServiceBase).IsAbstract);
        Assert.Contains(
            typeof(NodeLinkService.NodeLinkServiceClient).GetMethods(),
            method => method.Name == "Connect");
    }

    private static string[] BodyFields(MessageDescriptor message)
    {
        OneofDescriptor body = Assert.Single(message.Oneofs, oneof => oneof.Name == "body");
        return [.. body.Fields.Select(field => $"{field.Name}={field.FieldNumber}")];
    }
}
