using Akka.Actor;

using Aiakos.Contracts.Node.V1;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Orchestrator.Seats;

public sealed class SeatActor : ReceiveActor, IDisposable
{
    private const string InvalidSeatEvent = "INVALID_SEAT_EVENT";
    private const string SeatNotFound = "SEAT_NOT_FOUND";
    private const string HumanSeat = "HUMAN_SEAT";
    private const string SeatRetired = "SEAT_RETIRED";
    private const string SeatNodeMismatch = "SEAT_NODE_MISMATCH";
    private const string HarnessProfileUnavailable = "HARNESS_PROFILE_UNAVAILABLE";
    private const string SeatInputNotSupported = "SEAT_INPUT_NOT_SUPPORTED";
    private const string SeatActorUnavailable = "SEAT_ACTOR_UNAVAILABLE";
    private readonly SeatKey _key;
    private readonly ISeatActorReader _reader;
    private readonly ISeatActorWriter _writer;
    private readonly IReadOnlyList<IHarnessStateProfile> _profiles;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _actorStopping = new();
    private int _disposed;
    private SeatActorSnapshot? _snapshot;
    private string? _initializationFailure;

    public SeatActor(SeatKey key, ISeatActorReader reader, ISeatActorWriter writer,
        IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _key = key;
        _reader = reader;
        _writer = writer;
        _profiles = profiles;
        _timeProvider = timeProvider;

        ReceiveAsync<InitializeSeatActor>(_ => InitializeAsync());
        ReceiveAsync<SeatEventRequest>(HandleEventsAsync);
        ReceiveAsync<SeatInputRequest>(HandleInputAsync);
    }

    protected override void PreStart()
    {
        base.PreStart();
        Self.Tell(InitializeSeatActor.Instance);
    }

    protected override void PostRestart(Exception reason)
    {
        _snapshot = null;
        _initializationFailure = null;
        base.PostRestart(reason);
    }

    protected override void PostStop()
    {
        Dispose();
        base.PostStop();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _actorStopping.Cancel();
        _actorStopping.Dispose();
    }

    private async Task HandleEventsAsync(SeatEventRequest request)
    {
        try
        {
            if (await RejectIfInitializationFailedAsync(request.Completion))
            {
                return;
            }

            if (!IsValidKey(_key) || request.NodeInstanceId == Guid.Empty ||
                string.IsNullOrEmpty(request.NodeName) || request.Events is null ||
                request.Events.Any(static item => item is null))
            {
                Reject(request.Completion, InvalidSeatEvent);
                return;
            }

            foreach (var item in request.Events)
            {
                if (!IsValidEvent(item, _key.SeatId))
                {
                    Reject(request.Completion, InvalidSeatEvent);
                    return;
                }
            }

            var snapshot = await EnsureLoadedAsync();
            var routingError = ValidateSnapshot(snapshot);
            if (routingError is not null)
            {
                Reject(request.Completion, routingError);
                return;
            }

            if (!string.Equals(snapshot.Node, request.NodeName, StringComparison.Ordinal))
            {
                Reject(request.Completion, SeatNodeMismatch);
                return;
            }

            var profile = FindProfile(snapshot.Harness);
            if (profile is null)
            {
                Reject(request.Completion, HarnessProfileUnavailable);
                return;
            }

            if (request.Events.Count == 0)
            {
                request.Completion.TrySetResult(new SeatEventsCommitted(_key.SeatId,
                    request.NodeInstanceId, Math.Max(0, snapshot.State.NextSeq - 1)));
                return;
            }

            var inputSequences = request.Events.Select(static item => (long)item.Seq).ToArray();
            IReadOnlySet<long> committedSequences;
            try
            {
                committedSequences = await _reader.GetCommittedSequencesAsync(_key,
                    request.NodeInstanceId, inputSequences, _actorStopping.Token);
            }
            catch (OperationCanceledException) when (_actorStopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new RetryableActorFailureException();
            }

            var appliedAt = _timeProvider.GetUtcNow();
            var currentState = snapshot.State;
            var appliedInputs = new List<SeatAppliedInput>(request.Events.Count);
            foreach (var wireEvent in request.Events)
            {
                EventReceived input;
                try
                {
                    input = SeatWireInput.Map(request.NodeInstanceId, wireEvent, snapshot.Commands);
                }
                catch (Exception exception) when (exception is OverflowException or FormatException)
                {
                    Reject(request.Completion, InvalidSeatEvent);
                    return;
                }

                SeatStep step;
                if (committedSequences.Contains(input.Seq))
                {
                    step = Duplicate(currentState);
                }
                else
                {
                    if (HasInvalidKnownBodyTimestamp(wireEvent))
                    {
                        input = input with { Body = new ObservationGapBody(GapReason.IngestUnavailable) };
                    }
                    step = SeatStateMachine.Apply(currentState, input, profile, appliedAt);
                    var rotation = step.Effects.OfType<AdoptRotatedSession>().FirstOrDefault();
                    if (rotation is not null)
                    {
                        SeatNativeSession? owner;
                        try
                        {
                            owner = await _reader.FindNativeSessionAsync(_key.TenantId,
                                profile.Harness, rotation.NativeSessionId, _actorStopping.Token);
                        }
                        catch (OperationCanceledException) when (_actorStopping.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            throw new RetryableActorFailureException();
                        }

                        if (owner is not null && owner.SeatId != _key.SeatId)
                        {
                            input = input with { Body = new ObservationGapBody(GapReason.IngestUnavailable) };
                            var gapStep = SeatStateMachine.Apply(currentState, input, profile, appliedAt);
                            step = gapStep with
                            {
                                Disposition = EventDisposition.Evidence,
                                Findings = [.. gapStep.Findings,
                                    new FindingChange(SeatVocabulary.FindingSessionIdMismatch, true)]
                            };
                        }
                    }
                }

                appliedInputs.Add(new SeatAppliedInput(input, step, appliedAt, wireEvent, request.TraceParent));
                currentState = step.State;
            }

            await CommitAndRefreshAsync(snapshot, appliedInputs);
            var throughSequence = inputSequences.Max();
            request.Completion.TrySetResult(new SeatEventsCommitted(_key.SeatId,
                request.NodeInstanceId, throughSequence));
        }
        catch (ActorLoadFailedException)
        {
            request.Completion.TrySetException(new InvalidOperationException(SeatActorUnavailable));
            throw new InvalidOperationException(SeatActorUnavailable);
        }
        catch (OperationCanceledException) when (_actorStopping.IsCancellationRequested)
        {
            request.Completion.TrySetException(new InvalidOperationException(SeatActorUnavailable));
        }
        catch (RetryableActorFailureException)
        {
            request.Completion.TrySetException(new SeatCommitFailedException());
            throw new SeatCommitFailedException();
        }
        catch (RequestRejectedException exception)
        {
            request.Completion.TrySetException(new InvalidOperationException(exception.Code));
        }
        catch (SeatStoreRejectedException)
        {
            request.Completion.TrySetException(new InvalidOperationException("SEAT_COMMIT_REJECTED"));
        }
        catch (Exception exception)
        {
            request.Completion.TrySetException(exception);
        }
    }

    private async Task HandleInputAsync(SeatInputRequest request)
    {
        try
        {
            if (await RejectIfInitializationFailedAsync(request.Completion))
            {
                return;
            }

            if (!IsValidKey(_key))
            {
                Reject(request.Completion, InvalidSeatEvent);
                return;
            }

            var snapshot = await EnsureLoadedAsync();
            var routingError = ValidateSnapshot(snapshot);
            if (routingError is not null)
            {
                Reject(request.Completion, routingError);
                return;
            }

            var profile = FindProfile(snapshot.Harness);
            if (profile is null)
            {
                Reject(request.Completion, HarnessProfileUnavailable);
                return;
            }

            if (!IsSupportedInput(request.Input))
            {
                Reject(request.Completion, SeatInputNotSupported);
                return;
            }

            var appliedAt = _timeProvider.GetUtcNow();
            var step = SeatStateMachine.Apply(snapshot.State, request.Input, profile, appliedAt);
            var committedStep = WithoutAdoptEffect(step);
            var appliedInput = new SeatAppliedInput(request.Input, committedStep, appliedAt, null, null);
            var updated = await CommitAndRefreshAsync(snapshot, [appliedInput]);
            request.Completion.TrySetResult(new SeatInputCommitted(_key.SeatId, updated.Version,
                updated.State, committedStep.Effects));
        }
        catch (ActorLoadFailedException)
        {
            request.Completion.TrySetException(new InvalidOperationException(SeatActorUnavailable));
            throw new InvalidOperationException(SeatActorUnavailable);
        }
        catch (OperationCanceledException) when (_actorStopping.IsCancellationRequested)
        {
            request.Completion.TrySetException(new InvalidOperationException(SeatActorUnavailable));
        }
        catch (RetryableActorFailureException)
        {
            request.Completion.TrySetException(new SeatCommitFailedException());
            throw new SeatCommitFailedException();
        }
        catch (RequestRejectedException exception)
        {
            request.Completion.TrySetException(new InvalidOperationException(exception.Code));
        }
        catch (SeatStoreRejectedException)
        {
            request.Completion.TrySetException(new InvalidOperationException("SEAT_COMMIT_REJECTED"));
        }
        catch (Exception exception)
        {
            request.Completion.TrySetException(exception);
        }
    }

    private async Task<SeatActorSnapshot> EnsureLoadedAsync()
    {
        if (_snapshot is not null)
        {
            return _snapshot;
        }

        SeatActorSnapshot? snapshot;
        try
        {
            snapshot = await _reader.LoadAsync(_key, _actorStopping.Token);
        }
        catch (Exception)
        {
            throw new ActorLoadFailedException();
        }

        if (snapshot is null || snapshot.Key != _key)
        {
            throw new RequestRejectedException(SeatNotFound);
        }

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Context.Parent.Tell(new SeatChildLoaded(_key, snapshot.Version, ready));
        try
        {
            await ready.Task;
        }
        catch (Exception)
        {
            throw new ActorLoadFailedException();
        }

        _snapshot = snapshot;
        return snapshot;
    }

    private async Task InitializeAsync()
    {
        try
        {
            await EnsureLoadedAsync();
        }
        catch (RequestRejectedException exception)
        {
            _initializationFailure = exception.Code;
            Context.Parent.Tell(new SeatChildLoadFailed(_key));
        }
        catch (ActorLoadFailedException)
        {
            _initializationFailure = SeatActorUnavailable;
            Context.Parent.Tell(new SeatChildLoadFailed(_key));
        }
    }

    private Task<bool> RejectIfInitializationFailedAsync<T>(TaskCompletionSource<T> completion)
    {
        if (_initializationFailure is null)
        {
            return Task.FromResult(false);
        }

        if (_initializationFailure == SeatActorUnavailable)
        {
            completion.TrySetException(new InvalidOperationException(SeatActorUnavailable));
            throw new ActorLoadFailedException();
        }

        Reject(completion, _initializationFailure);
        return Task.FromResult(true);
    }

    private async Task<SeatActorSnapshot> CommitAndRefreshAsync(SeatActorSnapshot before,
        IReadOnlyList<SeatAppliedInput> appliedInputs)
    {
        try
        {
            await _writer.CommitAsync(before, appliedInputs, _actorStopping.Token);
            var refreshed = await _reader.LoadAsync(_key, _actorStopping.Token);
            if (refreshed is null || refreshed.Key != _key)
            {
                throw new InvalidOperationException();
            }

            _snapshot = refreshed;
            return refreshed;
        }
        catch (SeatStoreRejectedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (_actorStopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new RetryableActorFailureException();
        }
    }

    private static string? ValidateSnapshot(SeatActorSnapshot snapshot)
    {
        if (snapshot.Kind == "human")
        {
            return HumanSeat;
        }

        if (snapshot.Retired)
        {
            return SeatRetired;
        }

        if (!string.Equals(snapshot.Kind, "agent", StringComparison.Ordinal))
        {
            return SeatNotFound;
        }

        return null;
    }

    private IHarnessStateProfile? FindProfile(string? harness)
    {
        foreach (var profile in _profiles)
        {
            if (string.Equals(profile.Harness, harness, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        return null;
    }

    internal static bool IsSupportedInput(SeatInput? input) => input is NodeAttached or NodeLinkLost or
        OrchestratorRestarted or QuietTimeoutFired or LaunchWatchdogFired or UnknownProlongedFired;

    private static SeatStep Duplicate(SeatState state) => new(state, null, EventDisposition.Duplicate,
        Array.Empty<SeatTransition>(), Array.Empty<FindingChange>(), Array.Empty<SeatEffect>());

    private static SeatStep WithoutAdoptEffect(SeatStep step) => step with
    {
        Effects = step.Effects.Where(static effect => effect is not AdoptRotatedSession).ToArray()
    };

    internal static bool IsValidKey(SeatKey key) => key.TenantId != Guid.Empty && key.SeatId != Guid.Empty;

    internal static bool IsValidEvent(SeatEvent value, Guid seatId)
    {
        if (!Guid.TryParse(value.SeatId, out var eventSeatId) || eventSeatId != seatId ||
            value.Seq == 0 || value.Seq >= long.MaxValue || value.SourceSeq > long.MaxValue ||
            (!string.IsNullOrEmpty(value.LaunchId) && !Guid.TryParse(value.LaunchId, out _)) ||
            value.ObservedAt is not null && !IsValidTimestamp(value.ObservedAt))
        {
            return false;
        }

        return !HasIsolatedSurrogate(value);
    }

    private static bool IsValidTimestamp(Timestamp timestamp) =>
        timestamp.Seconds is >= -62_135_596_800 and <= 253_402_300_799 &&
        timestamp.Nanos is >= 0 and <= 999_999_999;

    private static bool HasInvalidKnownBodyTimestamp(SeatEvent value) => value.BodyCase switch
    {
        SeatEvent.BodyOneofCase.CommandResult => value.CommandResult.ResultCase switch
        {
            CommandResult.ResultOneofCase.Capture => HasInvalidTimestamp(value.CommandResult.Capture.CapturedAt),
            CommandResult.ResultOneofCase.Launch => value.CommandResult.Launch.Evidence is not null &&
                HasInvalidTimestamp(value.CommandResult.Launch.Evidence.CapturedAt),
            _ => false
        },
        SeatEvent.BodyOneofCase.Gap => HasInvalidTimestamp(value.Gap.From) ||
            HasInvalidTimestamp(value.Gap.To),
        _ => false
    };

    private static bool HasInvalidTimestamp(Timestamp? timestamp) =>
        timestamp is not null && !IsValidTimestamp(timestamp);

    private static bool HasIsolatedSurrogate(SeatEvent value)
    {
        if (HasIsolatedSurrogate(value.SeatId) || HasIsolatedSurrogate(value.LaunchId))
        {
            return true;
        }

        return value.BodyCase switch
        {
            SeatEvent.BodyOneofCase.Harness => HasIsolatedSurrogate(value.Harness),
            SeatEvent.BodyOneofCase.SessionObserved => HasIsolatedSurrogate(value.SessionObserved.NativeSessionId),
            SeatEvent.BodyOneofCase.CommandResult => HasIsolatedSurrogate(value.CommandResult),
            _ => false
        };
    }

    private static bool HasIsolatedSurrogate(HarnessEvent value) =>
        HasIsolatedSurrogate(value.Harness) || HasIsolatedSurrogate(value.NativeName) ||
        HasIsolatedSurrogate(value.NativeSessionId) || HasIsolatedSurrogate(value.RawContentType) ||
        value.Attributes.Any(static pair => HasIsolatedSurrogate(pair.Key) || HasIsolatedSurrogate(pair.Value)) ||
        value.Usage is not null && HasIsolatedSurrogate(value.Usage.ModelId);

    private static bool HasIsolatedSurrogate(CommandResult value)
    {
        if (HasIsolatedSurrogate(value.CommandId) || value.Error is not null && HasIsolatedSurrogate(value.Error))
        {
            return true;
        }

        return value.ResultCase switch
        {
            CommandResult.ResultOneofCase.Launch => HasIsolatedSurrogate(value.Launch),
            CommandResult.ResultOneofCase.Delivery => HasIsolatedSurrogate(value.Delivery.TurnId),
            CommandResult.ResultOneofCase.Capture => HasIsolatedSurrogate(value.Capture.Text),
            _ => false
        };
    }

    private static bool HasIsolatedSurrogate(LaunchResult value) =>
        HasIsolatedSurrogate(value.Reason) || HasIsolatedSurrogate(value.ObservedSessionId) ||
        value.Evidence is not null && HasIsolatedSurrogate(value.Evidence.Text);

    private static bool HasIsolatedSurrogate(Error value) => HasIsolatedSurrogate(value.Reason) ||
        HasIsolatedSurrogate(value.Message) || value.Metadata.Any(static pair =>
            HasIsolatedSurrogate(pair.Key) || HasIsolatedSurrogate(pair.Value));

    private static bool HasIsolatedSurrogate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static void Reject<T>(TaskCompletionSource<T> completion, string code) =>
        completion.TrySetException(new InvalidOperationException(code));

    private sealed record InitializeSeatActor
    {
        internal static InitializeSeatActor Instance { get; } = new();
    }

    private sealed class ActorLoadFailedException : Exception;

    private sealed class RetryableActorFailureException : Exception;

    private sealed class RequestRejectedException(string code) : Exception(code)
    {
        internal string Code { get; } = code;
    }
}
