using Aiakos.Contracts.Node.V1;

namespace Aiakos.Node.Commands;

public interface INodeCommandDriver
{
    string Harness { get; }

    IReadOnlyCollection<string> Capabilities { get; }

    Task<LaunchResult> StartAsync(string seatId, StartSeat start, CancellationToken ct);

    Task<DeliveryResult> DeliverAsync(string seatId, DeliverInput input, CancellationToken ct);

    Task SendKeysAsync(string seatId, SendKeys keys, CancellationToken ct);

    Task<PaneCapture> CaptureAsync(string seatId, CapturePane capture, CancellationToken ct);

    Task<StopResult> StopAsync(string seatId, StopSeat stopCommand, CancellationToken ct);
}

public sealed record NodeCommandSeat(string SeatId, string LaunchId, string Harness, bool Ready);
