using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;
using Google.Protobuf;
using System.Text;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class CommandValidatorTests
{
    private static readonly string[] Capabilities = [NodeCapabilities.HarnessClaudeCode, NodeCapabilities.CommandSendKeys, NodeCapabilities.LaunchFork, NodeCapabilities.SessionHostTmux];

    [Fact]
    public void ValidatesCommandBodiesAndLimits()
    {
        Assert.Equal(ErrorReasons.Unsupported, CommandValidator.Validate(new Command(), Capabilities)!.Reason);
        Assert.Null(CommandValidator.Validate(new Command { SendKeys = new SendKeys { Keys = { "Enter", "C-c" } } }, Capabilities));
        Assert.Equal(ErrorReasons.Unsupported, CommandValidator.Validate(new Command { SendKeys = new SendKeys { Keys = { "F1" } } }, Capabilities)!.Reason);
        Assert.Null(CommandValidator.Validate(new Command { DeliverInput = new DeliverInput { Body = new string('a', ContractLimits.MaxDeliverBodyBytes) } }, Capabilities));
        Assert.Equal(ErrorReasons.PayloadTooLarge, CommandValidator.Validate(new Command { DeliverInput = new DeliverInput { Body = new string('é', 524289) } }, Capabilities)!.Reason);
    }

    [Fact]
    public void ValidatesStartSeatInSpecifiedOrder()
    {
        StartSeat start = new() { Harness = "claude-code", Mode = LaunchMode.Fresh, Files = { new SeatFile { Root = FileRoot.SeatHome, Path = "a", Content = ByteString.CopyFromUtf8("x") } } };
        Assert.Null(CommandValidator.Validate(new Command { StartSeat = start }, Capabilities));
        start.Harness = "opencode";
        Assert.Equal(ErrorReasons.Unsupported, CommandValidator.Validate(new Command { StartSeat = start }, Capabilities)!.Reason);
        start.Harness = "claude-code";
        start.Files[0].Path = "../x";
        Assert.Equal(ErrorReasons.PathNotAllowed, CommandValidator.Validate(new Command { StartSeat = start }, Capabilities)!.Reason);
        start.Files[0].Path = "a";
        start.Secrets.Add(new SeatSecret());
        Assert.Equal(ErrorReasons.Unsupported, CommandValidator.Validate(new Command { StartSeat = start }, Capabilities)!.Reason);
    }
}
