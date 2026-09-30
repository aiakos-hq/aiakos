using System.Runtime.InteropServices;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aiakos.Node.Tests;

public sealed class SighupHandlerTests
{
    [Fact]
    public void HandlerCancelsTheDefaultHandlingAndStopsTheApplication()
    {
        var lifetime = new FakeLifetime();
        using var logs = new CapturingLoggerProvider();
        using var handler = new SighupHandler(lifetime, new Logger<SighupHandler>(new LoggerFactory([logs])));
        var context = new PosixSignalContext(PosixSignal.SIGHUP);

        handler.Handle(context);

        Assert.True(context.Cancel);
        Assert.Equal(1, lifetime.StopCalls);
        Assert.True(handler.Received);
        Assert.Contains(logs.Lines, line => line.Contains("SIGHUP", StringComparison.Ordinal) && line.Contains("gracefully", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegistrationStartsAndDisposesCleanly()
    {
        using var handler = new SighupHandler(new FakeLifetime(), new Logger<SighupHandler>(new LoggerFactory()));

        await handler.StartAsync(TestContext.Current.CancellationToken);
        await handler.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(handler.Received);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public int StopCalls { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopCalls++;
    }
}
