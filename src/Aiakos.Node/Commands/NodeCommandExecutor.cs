using System.Runtime.CompilerServices;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;

namespace Aiakos.Node.Commands;

public sealed class NodeCommandExecutor : IAsyncDisposable
{
    private readonly NodeEventBuffer _buffer;
    private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, DriverRegistration> _drivers;
    private readonly Dictionary<string, NodeCommandSeat> _seats = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> _capabilities;
    private readonly HashSet<Guid> _inflightCommands = [];
    private bool _disposed;

    public NodeCommandExecutor(
        NodeEventBuffer buffer,
        IReadOnlyList<INodeCommandDriver> drivers,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(drivers);

        _buffer = buffer;
        _ = timeProvider ?? TimeProvider.System;

        var registrations = new Dictionary<string, DriverRegistration>(StringComparer.Ordinal);
        var capabilities = new List<string>();
        var seenCapabilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var driver in drivers)
        {
            if (driver is null)
                throw InvalidDrivers();

            var harness = driver.Harness;
            var sourceCapabilities = driver.Capabilities;
            if (string.IsNullOrEmpty(harness) || sourceCapabilities is null || registrations.ContainsKey(harness))
                throw InvalidDrivers();

            var driverCapabilities = sourceCapabilities.ToArray();
            if (driverCapabilities.Any(static capability => capability is null))
                throw InvalidDrivers();

            registrations.Add(harness, new DriverRegistration(driver, harness, Array.AsReadOnly(driverCapabilities)));
            foreach (var capability in driverCapabilities)
                if (seenCapabilities.Add(capability))
                    capabilities.Add(capability);
        }

        _drivers = new System.Collections.ObjectModel.ReadOnlyDictionary<string, DriverRegistration>(registrations);
        _capabilities = Array.AsReadOnly(capabilities.ToArray());
    }

    public IReadOnlyList<string> Capabilities => _capabilities;

    public void RegisterSeat(NodeCommandSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (!TryCanonicalId(seat.SeatId, out var seatId) ||
            !TryCanonicalId(seat.LaunchId, out var launchId) ||
            string.IsNullOrEmpty(seat.Harness) ||
            !_drivers.TryGetValue(seat.Harness, out _))
            throw InvalidSeat();

        var existing = _buffer.Inventory().FirstOrDefault(inventory => StringComparer.Ordinal.Equals(inventory.SeatId, seatId));
        var inventory = existing is null
            ? new SeatInventory { SeatId = seatId }
            : existing.Clone();
        inventory.SeatId = seatId;
        inventory.LaunchId = launchId;
        inventory.Lifecycle = seat.Ready ? SessionLifecycle.Running : SessionLifecycle.Unknown;
        if (existing is null)
        {
            inventory.NativeSessionId = string.Empty;
            inventory.FirstBufferedSeq = 0;
            inventory.LastSeq = 0;
        }

        _buffer.RegisterSeat(inventory);
        _seats[seatId] = new NodeCommandSeat(seatId, launchId, seat.Harness, seat.Ready);
    }

    public CommandAck Receive(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        throw new NotSupportedException("Command admission is not available in this story.");
    }

    public void Acknowledge(EventAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ThrowIfDisposed();
        throw new NotSupportedException("Command acknowledgement is not available in this story.");
    }

    public int InflightCount => _inflightCommands.Count;

    public IAsyncEnumerable<ConnectRequest> ReadResponsesAsync(CancellationToken ct) => ReadResponsesCoreAsync(ct);

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<ConnectRequest> ReadResponsesCoreAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static bool TryCanonicalId(string? value, out string canonical)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            canonical = parsed.ToString("D");
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    private static ArgumentException InvalidDrivers() => new("Invalid command drivers.");

    private static ArgumentException InvalidSeat() => new("Invalid command seat.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record DriverRegistration(
        INodeCommandDriver Driver,
        string Harness,
        IReadOnlyList<string> Capabilities);
}
