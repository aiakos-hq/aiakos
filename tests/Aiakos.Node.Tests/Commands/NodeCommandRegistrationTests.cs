using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Commands;
using Aiakos.Node.Link;

namespace Aiakos.Node.Tests.Commands;

public sealed class NodeCommandRegistrationTests
{
    private const string SeatId = "11111111-1111-1111-1111-111111111111";
    private const string LaunchId = "22222222-2222-2222-2222-222222222222";
    private const string OtherLaunchId = "33333333-3333-3333-3333-333333333333";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] OrdinalCapabilities = ["capability-harness", "capability-Harness"];
    private static readonly string[] SingleCapability = ["before"];
    private static readonly NodeCommandSeat[] InvalidSeats =
    [
        new("", LaunchId, "terminal", Ready: true),
        new(SeatId, "00000000-0000-0000-0000-000000000000", "terminal", Ready: true),
        new(SeatId, LaunchId, "missing", Ready: true),
    ];

    [Fact]
    public void EmptyDriverCollectionIsAllowedAndHasNoCapabilities()
    {
        using var buffer = new NodeEventBuffer();
        var executor = new NodeCommandExecutor(buffer, []);

        Assert.Empty(executor.Capabilities);
    }

    [Fact]
    public void RejectsEmptyOrDuplicateHarnessRegistrationsOrdinally()
    {
        using var buffer = new NodeEventBuffer();

        var empty = Assert.Throws<ArgumentException>(() => new NodeCommandExecutor(buffer, [new Driver("")]));
        Assert.Equal("Invalid command drivers.", empty.Message);

        var duplicate = Assert.Throws<ArgumentException>(() => new NodeCommandExecutor(
            buffer, [new Driver("harness"), new Driver("harness")]));
        Assert.Equal("Invalid command drivers.", duplicate.Message);

        var ordinalDistinct = new NodeCommandExecutor(buffer, [new Driver("harness"), new Driver("Harness")]);
        Assert.Equal(OrdinalCapabilities, ordinalDistinct.Capabilities);
    }

    [Fact]
    public void DriverAndCapabilityCollectionsAreSnapshotted()
    {
        using var buffer = new NodeEventBuffer();
        var capabilitySource = new List<string> { "before" };
        var drivers = new List<INodeCommandDriver> { new MutableDriver("terminal", capabilitySource) };
        var executor = new NodeCommandExecutor(buffer, drivers);

        capabilitySource.Add("after");
        drivers.Clear();

        Assert.Equal(SingleCapability, executor.Capabilities);
    }

    [Fact]
    public void RegisterSeatCanonicalizesAndClonesNewInventory()
    {
        using var buffer = new NodeEventBuffer();
        var executor = new NodeCommandExecutor(buffer, [new Driver("terminal")]);
        var seat = new NodeCommandSeat(SeatId.ToUpperInvariant(), LaunchId.ToUpperInvariant(), "terminal", Ready: false);

        executor.RegisterSeat(seat);

        var inventory = Assert.Single(buffer.Inventory());
        Assert.Equal(SeatId, inventory.SeatId);
        Assert.Equal(LaunchId, inventory.LaunchId);
        Assert.Equal(SessionLifecycle.Unknown, inventory.Lifecycle);
        Assert.Equal(string.Empty, inventory.NativeSessionId);
        Assert.Equal((ulong)0, inventory.FirstBufferedSeq);
        Assert.Equal((ulong)0, inventory.LastSeq);
    }

    [Fact]
    public void ReadyRegistrationUsesRunningLifecycle()
    {
        using var buffer = new NodeEventBuffer();
        var executor = new NodeCommandExecutor(buffer, [new Driver("terminal")]);

        executor.RegisterSeat(new NodeCommandSeat(SeatId, LaunchId, "terminal", Ready: true));

        Assert.Equal(SessionLifecycle.Running, Assert.Single(buffer.Inventory()).Lifecycle);
    }

    [Fact]
    public void RegisterSeatPreservesExistingNativeSessionAndSequenceAuthority()
    {
        using var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(new SeatInventory
        {
            SeatId = SeatId,
            LaunchId = LaunchId,
            Lifecycle = SessionLifecycle.Running,
            NativeSessionId = "native-sentinel",
        });
        buffer.Enqueue(new SeatEvent
        {
            SeatId = SeatId,
            LaunchId = LaunchId,
            ObservedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(Now),
        });
        var executor = new NodeCommandExecutor(buffer, [new Driver("terminal")]);

        executor.RegisterSeat(new NodeCommandSeat(SeatId, OtherLaunchId, "terminal", Ready: false));

        var inventory = Assert.Single(buffer.Inventory());
        Assert.Equal(OtherLaunchId, inventory.LaunchId);
        Assert.Equal(SessionLifecycle.Unknown, inventory.Lifecycle);
        Assert.Equal("native-sentinel", inventory.NativeSessionId);
        Assert.Equal((ulong)1, inventory.FirstBufferedSeq);
        Assert.Equal((ulong)1, inventory.LastSeq);
    }

    [Fact]
    public void RejectsMalformedSeatRegistrationsWithSpecifiedMessage()
    {
        using var buffer = new NodeEventBuffer();
        var executor = new NodeCommandExecutor(buffer, [new Driver("terminal")]);

        foreach (var seat in InvalidSeats)
        {
            var error = Assert.Throws<ArgumentException>(() => executor.RegisterSeat(seat));
            Assert.Equal("Invalid command seat.", error.Message);
        }

        Assert.Empty(buffer.Inventory());
    }

    private sealed class Driver(string harness) : INodeCommandDriver
    {
        public string Harness { get; } = harness;
        public IReadOnlyCollection<string> Capabilities => [$"capability-{Harness}"];

        public Task<LaunchResult> StartAsync(string seatId, StartSeat start, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeliveryResult> DeliverAsync(string seatId, DeliverInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task SendKeysAsync(string seatId, SendKeys keys, CancellationToken ct) => throw new NotSupportedException();
        public Task<PaneCapture> CaptureAsync(string seatId, CapturePane capture, CancellationToken ct) => throw new NotSupportedException();
        public Task<StopResult> StopAsync(string seatId, StopSeat stop, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class MutableDriver(string harness, IReadOnlyCollection<string> capabilities) : INodeCommandDriver
    {
        public string Harness { get; } = harness;
        public IReadOnlyCollection<string> Capabilities { get; } = capabilities;

        public Task<LaunchResult> StartAsync(string seatId, StartSeat start, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeliveryResult> DeliverAsync(string seatId, DeliverInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task SendKeysAsync(string seatId, SendKeys keys, CancellationToken ct) => throw new NotSupportedException();
        public Task<PaneCapture> CaptureAsync(string seatId, CapturePane capture, CancellationToken ct) => throw new NotSupportedException();
        public Task<StopResult> StopAsync(string seatId, StopSeat stop, CancellationToken ct) => throw new NotSupportedException();
    }
}
