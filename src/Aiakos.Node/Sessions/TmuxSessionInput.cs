using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aiakos.Core;
using Microsoft.Extensions.Logging;

namespace Aiakos.Node.Sessions;

public sealed partial class TmuxSessionInput
{
    private const int MaximumCaptureBytes = 1048576;
    private static readonly TimeSpan MaximumSubmitDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private static readonly UTF8Encoding ReplacementUtf8 = new(false, false);
    private readonly TmuxClient _client;
    private readonly IProcessSnapshot _processes;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deliveryGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDeliveries = new(StringComparer.Ordinal);

    public TmuxSessionInput(TmuxClient client, IProcessSnapshot processes,
        ILogger<TmuxSessionInput> logger, IMeterFactory meterFactory, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(meterFactory);
        _client = client;
        _processes = processes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DeliveryReport> DeliverAsync(SessionHandle session, DeliveryRequest request,
        CancellationToken ct)
    {
        _client.EnsureAvailable();
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ValidateHandle(session, requireWritable: true);
        var lead = InputValidator.CheckLead(request.Lead);
        var body = InputValidator.CheckBody(request.Body);
        if (lead.Problem != InputProblem.None)
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.InputNotAllowed,
                lead.Reason ?? "Lead is not allowed.", false));
        if (body.Problem == InputProblem.NotAllowed)
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.InputNotAllowed,
                body.Reason ?? "Body is not allowed.", false));
        if (body.Problem == InputProblem.TooLarge)
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.PayloadTooLarge,
                body.Reason ?? "Body exceeds the allowed size.", false));
        if (request.Lead.Length == 0 && body.Normalized.Length == 0)
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.InputNotAllowed,
                "Lead and body cannot both be empty.", false));
        if (!Enum.IsDefined(request.Submit) || request.SubmitDelay < TimeSpan.Zero ||
            request.SubmitDelay > MaximumSubmitDelay)
            throw InvalidArgument("Invalid delivery request.");

        var gateKey = GateKey(session);
        var gate = _deliveryGates.GetOrAdd(gateKey, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
            throw Busy();

        using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_activeDeliveries.TryAdd(gateKey, deliveryCancellation))
        {
            gate.Release();
            throw Busy();
        }

        var token = deliveryCancellation.Token;
        var deliveryId = Guid.NewGuid().ToString("N");
        var submittedAt = _timeProvider.GetUtcNow();
        var stage = DeliveryStage.None;
        var confirmation = new Confirmation(ConfirmationOutcome.Unconfirmed, null);
        var resubmits = 0;
        string? resubmitReason = null;
        var deliveryContext = new TmuxDeliveryContext(this, session, deliveryId, submittedAt,
            () => resubmits++, value => resubmitReason = value, token);
        var bufferName = "aiakos-" + deliveryId[..8] + "-1";
        var bodyBytes = Encoding.UTF8.GetBytes(body.Normalized);
        var loaded = false;
        var pasted = false;
        SessionHostError? error = null;
        try
        {
            try
            {
                await VerifyMutableHandleAsync(session, CancellationToken.None).ConfigureAwait(false);
                await RunCheckedAsync(["load-buffer", "-b", bufferName, "-"], bodyBytes, CancellationToken.None)
                    .ConfigureAwait(false);
                loaded = true;
                stage = DeliveryStage.BufferLoaded;
                token.ThrowIfCancellationRequested();

                if (request.Lead.Length > 0)
                {
                    await VerifyMutableHandleAsync(session, CancellationToken.None).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    await RunCheckedAsync(["send-keys", "-t", session.PaneId, "-l", "--", request.Lead], null,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    stage = DeliveryStage.LeadTyped;
                    token.ThrowIfCancellationRequested();
                }

                if (body.Normalized.Length > 0)
                {
                    await VerifyMutableHandleAsync(session, CancellationToken.None).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    await RunCheckedAsync(["paste-buffer", "-p", "-r", "-d", "-b", bufferName, "-t", session.PaneId], null,
                        CancellationToken.None).ConfigureAwait(false);
                    stage = DeliveryStage.BodyPasted;
                    pasted = true;
                    token.ThrowIfCancellationRequested();
                }

                if (request.SubmitDelay > TimeSpan.Zero)
                    await Task.Delay(request.SubmitDelay, _timeProvider, token).WaitAsync(token).ConfigureAwait(false);

                if (request.Submit == SubmitKey.CtrlM)
                {
                    await VerifyMutableHandleAsync(session, CancellationToken.None).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    await RunCheckedAsync(["send-keys", "-t", session.PaneId, "C-m"], null, CancellationToken.None)
                        .ConfigureAwait(false);
                    stage = DeliveryStage.Submitted;
                    submittedAt = _timeProvider.GetUtcNow();
                    token.ThrowIfCancellationRequested();
                }

                token.ThrowIfCancellationRequested();
                if (request.Confirmer is null)
                {
                    confirmation = new Confirmation(ConfirmationOutcome.NotRequested, null);
                }
                else
                {
                    confirmation = await request.Confirmer.ConfirmAsync(deliveryContext, token)
                        .WaitAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                error = CancelledError();
                confirmation = new Confirmation(ConfirmationOutcome.Unconfirmed, null);
            }
            catch (SessionHostException exception) when (loaded)
            {
                error = exception.Error with { Retryable = false };
                confirmation = new Confirmation(ConfirmationOutcome.Unconfirmed, null);
            }

            if (loaded && !pasted)
            {
                try
                {
                    await _client.RunAsync(["delete-buffer", "-b", bufferName], null, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception) { }
            }

            return new DeliveryReport(deliveryId, stage, confirmation, resubmits, body.Bytes,
                body.LineEndingsNormalized, error, resubmitReason);
        }
        finally
        {
            _activeDeliveries.TryRemove(new KeyValuePair<string, CancellationTokenSource>(gateKey,
                deliveryCancellation));
            gate.Release();
        }
    }

    public async Task SendKeysAsync(SessionHandle session, IReadOnlyList<NamedKey> keys, CancellationToken ct)
    {
        _client.EnsureAvailable();
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(keys);
        ValidateHandle(session, requireWritable: true);
        ct.ThrowIfCancellationRequested();
        if (keys.Count > 32 || keys.Any(static key => !Enum.IsDefined(key)))
            throw InvalidArgument("Keys must contain at most 32 defined key values.");

        var gate = _deliveryGates.GetOrAdd(GateKey(session), static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct).ConfigureAwait(false))
            throw Busy();
        try
        {
            await VerifyMutableHandleAsync(session, ct).ConfigureAwait(false);
            if (keys.Count == 0)
                return;
            var arguments = new List<string> { "send-keys", "-t", session.PaneId };
            arguments.AddRange(keys.Select(KeyName));
            await RunCheckedAsync(arguments, null, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaneSnapshot> CaptureAsync(SessionHandle session, CaptureRequest request, CancellationToken ct)
    {
        _client.EnsureAvailable();
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ValidateHandle(session, requireWritable: false);
        ct.ThrowIfCancellationRequested();
        if (request.HistoryLines is < 0 or > 10000 || request.MaxBytes is < 1 or > MaximumCaptureBytes)
            throw InvalidCaptureRequest();

        var metadataResult = await _client.RunAsync(["display-message", "-p", "-t", session.PaneId,
            "#{pane_pid}	#{@aiakos-launch}	#{pane_width}	#{pane_height}	#{cursor_x}	#{cursor_y}	#{pane_dead}"],
            null, ct).ConfigureAwait(false);
        var metadata = ParseCaptureMetadata(metadataResult, session);
        if (!metadata.PaneDead)
            await VerifyProcessIdentityAsync(session, ct).ConfigureAwait(false);

        var captureArguments = new List<string> { "capture-pane", "-p", "-t", session.PaneId };
        if (request.HistoryLines > 0)
        {
            captureArguments.Add("-S");
            captureArguments.Add("-" + request.HistoryLines.ToString(CultureInfo.InvariantCulture));
        }
        var captureResult = await _client.RunCaptureAsync(captureArguments, ct).ConfigureAwait(false);
        ThrowForCommandFailure(captureResult);

        var bytes = captureResult.Stdout;
        if (captureResult.StdoutTruncated)
        {
            var newline = Array.IndexOf(bytes, (byte)'\n');
            bytes = newline < 0 ? [] : bytes.AsSpan(newline + 1).ToArray();
        }

        var lines = NormalizeLines(ReplacementUtf8.GetString(bytes), metadata.Size.Rows);
        var truncated = captureResult.StdoutTruncated;
        while (Utf8Bytes(lines) > request.MaxBytes && lines.Any(static line => !line.Visible))
        {
            lines.RemoveAt(lines.FindIndex(static line => !line.Visible));
            truncated = true;
        }
        if (Utf8Bytes(lines) > request.MaxBytes)
            truncated = true;

        return new PaneSnapshot(string.Join('\n', lines.Select(static line => line.Text)), truncated, lines.Count,
            _timeProvider.GetUtcNow(),
            metadata.Size, (metadata.CursorX, metadata.CursorY), metadata.PaneDead);
    }

    public void CancelDelivery(SessionHandle session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_activeDeliveries.TryGetValue(GateKey(session), out var active))
        {
            try { active.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    internal async Task VerifyMutableHandleAsync(SessionHandle session, CancellationToken ct)
    {
        _client.EnsureAvailable();
        ArgumentNullException.ThrowIfNull(session);
        ValidateHandle(session, requireWritable: true);
        ct.ThrowIfCancellationRequested();

        var result = await _client.RunAsync(["display-message", "-p", "-t", session.PaneId,
            "#{pane_pid}	#{@aiakos-launch}	#{pane_dead}"], null, ct).ConfigureAwait(false);
        var values = ReadOutput(result).Split('\t');
        if (values.Length != 3 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture,
                out var panePid) || panePid != session.PanePid ||
            !StringComparer.Ordinal.Equals(values[1], session.LaunchId) || values[2] != "0")
            throw NotFound();

        await VerifyProcessIdentityAsync(session, ct).ConfigureAwait(false);
    }

    private async Task ResubmitAsync(SessionHandle session, string reason, Func<bool> alreadyResubmitted,
        Action markResubmitted, Action incrementCount, Action<string?> setReason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (alreadyResubmitted())
            throw new InvalidOperationException("Only one resubmit is allowed for a delivery.");
        // The delivery already owns this gate; its confirmer is the only caller of ResubmitAsync.
        markResubmitted();
        await VerifyMutableHandleAsync(session, ct).ConfigureAwait(false);
        await RunCheckedAsync(["send-keys", "-t", session.PaneId, "C-m"], null, ct).ConfigureAwait(false);
        incrementCount();
        setReason(reason);
    }

    private async Task VerifyProcessIdentityAsync(SessionHandle session, CancellationToken ct)
    {
        var processes = await _processes.ReadAsync(ct).ConfigureAwait(false);
        if (!processes.Any(process => process.Pid == session.PanePid && process.StartTime == session.PaneStartTime))
            throw NotFound();
    }

    private static CaptureMetadata ParseCaptureMetadata(ProcessResult result, SessionHandle session)
    {
        ThrowForCommandFailure(result);
        var fields = ReadOutput(result).Split('\t');
        if (fields.Length != 7 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture,
                out var pid) || pid != session.PanePid || !StringComparer.Ordinal.Equals(fields[1], session.LaunchId) ||
            !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var width) || width <= 0 ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height <= 0 ||
            !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var cursorX) ||
            cursorX < 0 || cursorX > width ||
            !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var cursorY) ||
            cursorY < 0 || cursorY >= height || fields[6] is not ("0" or "1"))
            throw NotFound();

        return new CaptureMetadata(new TerminalSize(width, height), cursorX, cursorY, fields[6] == "1");
    }

    private static List<CaptureLine> NormalizeLines(string text, int visibleRows)
    {
        if (text.Length == 0)
            return [];

        var physicalLines = text.Split('\n').ToList();
        if (text.EndsWith('\n'))
            physicalLines.RemoveAt(physicalLines.Count - 1);
        var visibleStart = Math.Max(0, physicalLines.Count - visibleRows);
        var lines = new List<CaptureLine>(physicalLines.Count);
        for (var index = 0; index < physicalLines.Count; index++)
        {
            var builder = new StringBuilder(physicalLines[index].Length);
            foreach (var character in physicalLines[index])
                if (character == '\t' || character > '\u001f' && character is not (>= '\u007f' and <= '\u009f'))
                    builder.Append(character);
            lines.Add(new CaptureLine(builder.ToString().TrimEnd(), index >= visibleStart));
        }
        while (lines.Count > 0 && lines[^1].Text.Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static int Utf8Bytes(IReadOnlyList<CaptureLine> lines) =>
        Encoding.UTF8.GetByteCount(string.Join('\n', lines.Select(static line => line.Text)));

    private async Task<ProcessResult> RunCheckedAsync(IReadOnlyList<string> command, ReadOnlyMemory<byte>? stdin,
        CancellationToken ct)
    {
        var result = await _client.RunAsync(command, stdin, ct).ConfigureAwait(false);
        ThrowForCommandFailure(result);
        return result;
    }

    private static void ThrowForCommandFailure(ProcessResult result)
    {
        var error = TmuxClient.GetError(result);
        if (error is not null)
            throw new SessionHostException(error);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.TmuxFailed,
                "tmux invocation failed.", true));
    }

    private static string ReadOutput(ProcessResult result)
    {
        ThrowForCommandFailure(result);
        var text = ReplacementUtf8.GetString(result.Stdout);
        return text.EndsWith('\n') ? text[..^1].TrimEnd('\r') : text;
    }

    private static void ValidateHandle(SessionHandle session, bool requireWritable)
    {
        if (string.IsNullOrEmpty(session.PaneId) || !PaneIdRegex().IsMatch(session.PaneId) ||
            session.PanePid <= 0 || string.IsNullOrEmpty(session.LaunchId) ||
            session.PaneStartTime == 0 || requireWritable && session.ReadOnly)
            throw NotFound();
    }

    private static string GateKey(SessionHandle session) => session.PaneId + "\0" + session.LaunchId;

    private static string KeyName(NamedKey key) => key switch
    {
        NamedKey.Enter => "Enter",
        NamedKey.Escape => "Escape",
        NamedKey.Tab => "Tab",
        NamedKey.Up => "Up",
        NamedKey.Down => "Down",
        NamedKey.Left => "Left",
        NamedKey.Right => "Right",
        NamedKey.CtrlC => "C-c",
        NamedKey.CtrlD => "C-d",
        _ => throw InvalidArgument("Key value is not defined.")
    };

    private static SessionHostException NotFound() =>
        new(new SessionHostError(SessionHostErrorCode.NotFound, "Session was not found.", false));

    private static SessionHostException Busy() =>
        new(new SessionHostError(SessionHostErrorCode.Busy, "A delivery is already in progress for this session.", true));

    private static SessionHostError CancelledError() =>
        new(SessionHostErrorCode.TmuxFailed, "Delivery was cancelled.", false);

    private static SessionHostException InvalidCaptureRequest() =>
        InvalidArgument("Invalid capture request.");

    private static SessionHostException InvalidArgument(string message) =>
        new(new SessionHostError(SessionHostErrorCode.InvalidArgument, message, false));

    [GeneratedRegex(@"\A%[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PaneIdRegex();

    private sealed record CaptureMetadata(TerminalSize Size, int CursorX, int CursorY, bool PaneDead);

    private sealed record CaptureLine(string Text, bool Visible);

    private sealed class TmuxDeliveryContext : DeliveryContext
    {
        private readonly TmuxSessionInput _owner;
        private readonly SessionHandle _session;
        private readonly CancellationToken _deliveryToken;
        private readonly Action _incrementResubmits;
        private readonly Action<string?> _setReason;
        private bool _resubmitted;

        public TmuxDeliveryContext(TmuxSessionInput owner, SessionHandle session, string deliveryId,
            DateTimeOffset submittedAt, Action incrementResubmits, Action<string?> setReason,
            CancellationToken deliveryToken)
        {
            _owner = owner;
            _session = session;
            DeliveryId = deliveryId;
            SubmittedAt = submittedAt;
            _deliveryToken = deliveryToken;
            _incrementResubmits = incrementResubmits;
            _setReason = setReason;
        }

        public override string DeliveryId { get; }
        public override DateTimeOffset SubmittedAt { get; }

        public override Task<PaneSnapshot> CaptureAsync(CaptureRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _deliveryToken.ThrowIfCancellationRequested();
            return _owner.CaptureAsync(_session, request, ct);
        }

        public override Task ResubmitAsync(string reason, CancellationToken ct) =>
            _owner.ResubmitAsync(_session, reason, () => _resubmitted,
                () => _resubmitted = true, _incrementResubmits, _setReason, ct);
    }
}
