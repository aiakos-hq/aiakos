using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Sdk;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class TestKitCompatibilityTests
{
    [Fact]
    public async Task TestProbeReceivesPingAndActorSystemTerminates()
    {
        var system = ActorSystem.Create($"testkit-compatibility-{Guid.NewGuid():N}");

        try
        {
            var testKit = new SmokeTestKit(system);
            var echo = system.ActorOf(Props.Create(() => new EchoActor()));
            var probe = testKit.CreateTestProbe();

            probe.Send(echo, "ping");
            var received = probe.ExpectMsg<string>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("ping", received);

            var assertions = new XunitV3TestKitAssertions();
            var failure = Assert.ThrowsAny<XunitException>(() =>
                assertions.AssertTrue(false, "expected {0}", "failure"));
            Assert.Contains("expected failure", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            await system.Terminate().WaitAsync(TestContext.Current.CancellationToken);
            await system.WhenTerminated.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void AdapterCoversTestKitAssertionOperations()
    {
        var assertions = new XunitV3TestKitAssertions();

        assertions.AssertTrue(true, "true condition");
        assertions.AssertFalse(false, "false condition");
        assertions.AssertEqual(7, 7, "equal values");
        assertions.AssertEqual("ping", "PING", StringComparer.OrdinalIgnoreCase.Equals, "comparer values");

        Assert.ThrowsAny<XunitException>(() => assertions.Fail("explicit failure"));
        Assert.ThrowsAny<XunitException>(() => assertions.AssertFalse(true, "expected false"));
        Assert.ThrowsAny<XunitException>(() => assertions.AssertEqual(7, 8, "expected {0}", 7));
        Assert.ThrowsAny<XunitException>(() => assertions.AssertEqual("ping", "pong", StringComparer.Ordinal.Equals, "not equal"));
    }

    private sealed class SmokeTestKit(ActorSystem system) : TestKitBase(new XunitV3TestKitAssertions(), system, "smoke-probe");

    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor()
        {
            ReceiveAny(message => Sender.Tell(message));
        }
    }
}
