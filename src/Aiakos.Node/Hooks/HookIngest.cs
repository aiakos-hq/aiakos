using System.Globalization;
using System.Net;
using Aiakos.Contracts.Node.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiakos.Node.Hooks;

public sealed class HookIngest : IAsyncDisposable
{
    private readonly int _port;
    private readonly HookLaunchRegistry _registry;
    private readonly IHookIngestConsumer _consumer;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private WebApplication? _app;
    private Uri? _baseUri;
    private readonly object _sequenceGate = new();
    private readonly Dictionary<string, ulong> _sequences = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gapped = new(StringComparer.Ordinal);

    public HookIngest(int port, HookLaunchRegistry registry, IHookIngestConsumer consumer,
        TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public Uri BaseUri => _baseUri ?? throw new InvalidOperationException("Hook ingest has not started.");

    public async Task StartAsync(CancellationToken ct)
    {
        if (_app is not null) throw new InvalidOperationException("Hook ingest is already started.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new DelegatingLoggerProvider(_loggerFactory));
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, _port));
        var app = builder.Build();
        app.MapPost("/v1/hooks/{kind}/{name}", HandleAsync);
        await app.StartAsync(ct).ConfigureAwait(false);
        _app = app;
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses;
        var address = addresses.Single();
        _baseUri = new Uri(address.TrimEnd('/') + "/v1/hooks");
    }

    private async Task HandleAsync(HttpContext context)
    {
        var token = context.Request.Headers.Authorization.ToString();
        if (!_registry.TryResolve(token, out var launch) || launch is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var kind = context.Request.RouteValues["kind"]?.ToString() ?? "";
        var name = context.Request.RouteValues["name"]?.ToString() ?? "";
        if (kind is not ("hook" or "status") || !ValidName(name))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var body = await ReadBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        var seqText = context.Request.Headers["X-Aiakos-Source-Seq"].ToString();
        var seq = ulong.TryParse(seqText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var received = _timeProvider.GetUtcNow();
        var gap = false;
        lock (_sequenceGate)
        {
            var hadPrevious = _sequences.TryGetValue(launch.LaunchId, out var previous);
            if (seq > 0 && ((!hadPrevious && seq > 1) || (hadPrevious && previous < ulong.MaxValue && seq > previous + 1)) && _gapped.Add(launch.LaunchId)) gap = true;
            if (seq > previous) _sequences[launch.LaunchId] = seq;
        }
        if (gap)
            await _consumer.GapAsync(launch, new ObservationGap { Reason = GapReason.IngestUnavailable, DroppedEvents = 0 }, context.RequestAborted).ConfigureAwait(false);
        await _consumer.ReceiveAsync(new IngestedPayload(launch, kind, name, seq, received, body), context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static bool ValidName(string name) => name.Length is > 0 and <= 128 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var output = new MemoryStream();
        await request.Body.CopyToAsync(output, ct).ConfigureAwait(false);
        return output.ToArray();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_app is not { } app) return;
        _app = null;
        _baseUri = null;
        await app.StopAsync(ct).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private sealed class DelegatingLoggerProvider(ILoggerFactory loggerFactory) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => loggerFactory.CreateLogger(categoryName);
        public void Dispose() { }
    }
}
