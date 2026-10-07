using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;

using Grpc.Core;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeLinkService(
    NodeTokenRegistry tokens,
    IOptions<NodeLinkOptions> options,
    INodeLinkApplication application,
    NodeLinkRegistry registry,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime) : Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceBase
{
    public override async Task Connect(
        IAsyncStreamReader<ConnectRequest> requestStream,
        IServerStreamWriter<ConnectResponse> responseStream,
        ServerCallContext context)
    {
        var authorization = context.RequestHeaders
            .Where(static entry => string.Equals(entry.Key, "authorization", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var identity = authorization.Length == 1 ? tokens.Authenticate(authorization[0].Value) : null;
        if (identity is null)
            throw Failure(StatusCode.Unauthenticated, "Node authentication failed.");

        var firstEnvelope = await ReadHelloAsync(requestStream, context, options.Value.HelloTimeout).ConfigureAwait(false);
        var hello = firstEnvelope.Hello;
        ProtocolVersion protocol;
        IReadOnlyList<ReplayFrom> replay;
        using (NodeLinkTelemetry.StartReceiveActivity(firstEnvelope.Trace))
        {
            if (!string.Equals(hello.NodeName, identity.NodeName, StringComparison.Ordinal))
                throw Failure(StatusCode.PermissionDenied, "Node name does not match registration.");

            var negotiatedProtocol = NodeProtocol.Negotiate(hello.Protocol, NodeProtocol.Current);
            if (negotiatedProtocol is null)
                throw Failure(StatusCode.FailedPrecondition, "Unsupported node protocol.");
            protocol = negotiatedProtocol;

            if (!Guid.TryParse(hello.NodeInstanceId, out var instanceId) || instanceId == Guid.Empty)
                throw Failure(StatusCode.FailedPrecondition, "Invalid node instance ID.");
            hello.NodeInstanceId = instanceId.ToString("D");

            try
            {
                replay = await application.GetReplayAsync(identity, hello, context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (context.CancellationToken.IsCancellationRequested)
            {
                throw Failure(StatusCode.Cancelled, string.Empty);
            }
            catch (Exception)
            {
                throw Failure(StatusCode.Internal, "Node link processing failed.");
            }
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        NodeLinkSession? session = null;
        CancellationTokenSource? callbackCancellation = null;
        using var writeGate = new SemaphoreSlim(1, 1);
        var connected = false;
        var disconnected = false;
        var unknown = false;
        var stateTail = Task.CompletedTask;
        Task stateChanged(NodeLinkState state) => stateTail = stateTail.ContinueWith(
            async previousState =>
            {
                if (application is not INodeEventApplication)
                {
                    try { await application.StateChangedAsync(identity, state, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception) { }
                    return;
                }

                using var deadline = new CancellationTokenSource(options.Value.LivenessTimeout, timeProvider);
                var sessionToken = session?.CallbackToken ?? CancellationToken.None;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    deadline.Token, context.CancellationToken, lifetime.ApplicationStopping, sessionToken);
                Task? callback = null;
                try { callback = application.StateChangedAsync(identity, state, linked.Token); }
                catch (Exception) { }
                if (callback is null)
                    return;
                try { await callback.WaitAsync(linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    _ = callback.ContinueWith(static task => _ = task.Exception,
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception) { }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();

        async Task writeGoodbye(GoodbyeReason reason, string message)
        {
            await writeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var response = new ConnectResponse
                {
                    Goodbye = new Goodbye { Reason = reason, Message = message },
                };
                NodeLinkTelemetry.SetTrace(response);
                await responseStream.WriteAsync(response).ConfigureAwait(false);
            }
            finally { writeGate.Release(); }
        }

        Task shutdownTask = Task.Delay(Timeout.InfiniteTimeSpan, lifetime.ApplicationStopping);
        Task? livenessTask = null;
        CancellationTokenSource? livenessSource = null;
        long livenessStartedAt = 0;
        void armLiveness(TimeSpan delay)
        {
            livenessSource?.Cancel();
            livenessSource?.Dispose();
            livenessSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            livenessTask = Task.Delay(delay, timeProvider, livenessSource.Token);
        }
        void resetLiveness()
        {
            livenessStartedAt = timeProvider.GetTimestamp();
            armLiveness(options.Value.LivenessTimeout);
        }
        async Task<bool> readNext(NodeLinkSession current)
        {
            var readTask = requestStream.MoveNext(context.CancellationToken);
            while (true)
            {
                var activeLiveness = livenessTask;
                var completed = await Task.WhenAny(readTask,
                    activeLiveness ?? Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken),
                    current.Superseded, shutdownTask).ConfigureAwait(false);
                if (completed == current.Superseded)
                {
                    await writeGoodbye(GoodbyeReason.Superseded, "Node link superseded.").ConfigureAwait(false);
                    throw Failure(StatusCode.Aborted, "Node link superseded.");
                }
                if (completed == shutdownTask)
                {
                    await writeGoodbye(GoodbyeReason.Shutdown, "Orchestrator shutting down.").ConfigureAwait(false);
                    throw Failure(StatusCode.Unavailable, "Orchestrator shutting down.");
                }
                if (activeLiveness is not null && completed == activeLiveness)
                {
                    livenessTask = null;
                    livenessSource?.Dispose();
                    livenessSource = null;
                    var elapsed = timeProvider.GetElapsedTime(livenessStartedAt);
                    if (elapsed < options.Value.LivenessTimeout)
                    {
                        armLiveness(options.Value.LivenessTimeout - elapsed);
                        continue;
                    }
                    if (!unknown)
                    {
                        unknown = true;
                        _ = stateChanged(NodeLinkState.Unknown);
                    }
                    continue;
                }

                try { return await readTask.ConfigureAwait(false); }
                catch (Exception) when (context.CancellationToken.IsCancellationRequested)
                { throw Failure(StatusCode.Cancelled, string.Empty); }
            }
        }
        async Task waitForCallback(Task callbackTask, NodeLinkSession current)
        {
            async Task endAsSuperseded()
            {
                _ = callbackTask.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                await writeGoodbye(GoodbyeReason.Superseded, "Node link superseded.").ConfigureAwait(false);
                throw Failure(StatusCode.Aborted, "Node link superseded.");
            }

            while (true)
            {
                // Supersession is authoritative even when cancellation completes the callback
                // task at the same time and wins Task.WhenAny's tie.
                if (current.Superseded.IsCompleted)
                    await endAsSuperseded().ConfigureAwait(false);

                if (callbackTask.IsCompleted)
                {
                    try
                    {
                        await callbackTask.ConfigureAwait(false);
                        return;
                    }
                    catch (Exception) when (current.Superseded.IsCompleted)
                    {
                        await endAsSuperseded().ConfigureAwait(false);
                    }
                }

                var activeLiveness = livenessTask;
                var completed = await Task.WhenAny(callbackTask,
                    activeLiveness ?? Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken),
                    current.Superseded, shutdownTask).ConfigureAwait(false);
                if (current.Superseded.IsCompleted)
                    await endAsSuperseded().ConfigureAwait(false);
                if (completed == shutdownTask)
                {
                    _ = callbackTask.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    await writeGoodbye(GoodbyeReason.Shutdown, "Orchestrator shutting down.").ConfigureAwait(false);
                    throw Failure(StatusCode.Unavailable, "Orchestrator shutting down.");
                }
                if (activeLiveness is not null && completed == activeLiveness)
                {
                    livenessTask = null;
                    livenessSource?.Dispose();
                    livenessSource = null;
                    var elapsed = timeProvider.GetElapsedTime(livenessStartedAt);
                    if (elapsed < options.Value.LivenessTimeout)
                    {
                        armLiveness(options.Value.LivenessTimeout - elapsed);
                        continue;
                    }
                    if (!unknown)
                    {
                        unknown = true;
                        _ = stateChanged(NodeLinkState.Unknown);
                    }
                }
            }
        }

        async Task<EventAck?> waitForEventCallback(Task<EventAck?> callbackTask, NodeLinkSession current,
            CancellationTokenSource eventCallbackCancellation)
        {
            var requestCanceled = Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);

            void observeLateCompletion() => _ = callbackTask.ContinueWith(
                static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);

            async Task endAsSuperseded()
            {
                eventCallbackCancellation.Cancel();
                observeLateCompletion();
                await writeGoodbye(GoodbyeReason.Superseded, "Node link superseded.").ConfigureAwait(false);
                throw Failure(StatusCode.Aborted, "Node link superseded.");
            }

            async Task endAsShutdown()
            {
                eventCallbackCancellation.Cancel();
                observeLateCompletion();
                await writeGoodbye(GoodbyeReason.Shutdown, "Orchestrator shutting down.").ConfigureAwait(false);
                throw Failure(StatusCode.Unavailable, "Orchestrator shutting down.");
            }

            while (true)
            {
                if (current.Superseded.IsCompleted)
                    await endAsSuperseded().ConfigureAwait(false);
                if (shutdownTask.IsCompleted)
                    await endAsShutdown().ConfigureAwait(false);
                if (context.CancellationToken.IsCancellationRequested)
                {
                    eventCallbackCancellation.Cancel();
                    observeLateCompletion();
                    throw Failure(StatusCode.Cancelled, string.Empty);
                }

                var activeLiveness = livenessTask;
                if (activeLiveness is not null && activeLiveness.IsCompleted)
                {
                    livenessTask = null;
                    livenessSource?.Dispose();
                    livenessSource = null;
                    var elapsed = timeProvider.GetElapsedTime(livenessStartedAt);
                    if (elapsed < options.Value.LivenessTimeout)
                    {
                        armLiveness(options.Value.LivenessTimeout - elapsed);
                        continue;
                    }
                    if (!unknown)
                    {
                        unknown = true;
                        _ = stateChanged(NodeLinkState.Unknown);
                    }
                    eventCallbackCancellation.Cancel();
                    observeLateCompletion();
                    throw Failure(StatusCode.Unavailable, "Node event commit timed out.");
                }

                if (callbackTask.IsCompleted)
                {
                    try { return await callbackTask.ConfigureAwait(false); }
                    catch (Exception) when (current.Superseded.IsCompleted || shutdownTask.IsCompleted ||
                                            context.CancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }
                    catch (Exception)
                    {
                        throw Failure(StatusCode.FailedPrecondition, "Node event processing failed.");
                    }
                }

                await Task.WhenAny(callbackTask, activeLiveness ?? Task.Delay(Timeout.InfiniteTimeSpan),
                    current.Superseded, shutdownTask, requestCanceled).ConfigureAwait(false);
            }
        }

        async Task writeEventAcknowledgement(EventAck acknowledgement)
        {
            await writeGate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var response = new ConnectResponse { EventAck = acknowledgement.Clone() };
                NodeLinkTelemetry.SetTrace(response);
                await responseStream.WriteAsync(response).ConfigureAwait(false);
            }
            finally { writeGate.Release(); }
        }

        try
        {
            var linkOptions = options.Value;
            var welcome = new Welcome
            {
                Protocol = protocol,
                NodeId = identity.NodeId,
                ConnectionId = Guid.NewGuid().ToString("D"),
                HeartbeatInterval = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(linkOptions.HeartbeatInterval),
                LivenessTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(linkOptions.LivenessTimeout),
                ServerTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
                Limits = new Limits { MaxMessageBytes = 4194304, MaxInflightCommands = 64, EventBufferCapacity = 10000 },
            };
            welcome.Replay.AddRange(replay);
            var response = new ConnectResponse { Welcome = welcome };
            NodeLinkTelemetry.SetTrace(response);
            await responseStream.WriteAsync(response).ConfigureAwait(false);
            session = registry.Register(identity, hello.NodeInstanceId);
            callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken, session.CallbackToken);
            connected = true;
            resetLiveness();
            _ = stateChanged(NodeLinkState.Connected);

            while (true)
            {
                var activeSession = session;
                bool hasMessage = await readNext(activeSession).ConfigureAwait(false);
                if (!hasMessage)
                {
                    disconnected = true;
                    await stateChanged(NodeLinkState.Disconnected).ConfigureAwait(false);
                    await stateTail.ConfigureAwait(false);
                    return;
                }

                var request = requestStream.Current;
                using (NodeLinkTelemetry.StartReceiveActivity(request.Trace))
                {
                    resetLiveness();
                    if (unknown)
                    {
                        unknown = false;
                        _ = stateChanged(NodeLinkState.Connected);
                    }

                    if (request.BodyCase == ConnectRequest.BodyOneofCase.Goodbye)
                    {
                        disconnected = true;
                        await stateChanged(NodeLinkState.Disconnected).ConfigureAwait(false);
                        await stateTail.ConfigureAwait(false);
                        return;
                    }
                    if (request.BodyCase == ConnectRequest.BodyOneofCase.Heartbeat)
                        continue;
                    if (request.BodyCase is not (ConnectRequest.BodyOneofCase.CommandAck or ConnectRequest.BodyOneofCase.SeatEvent))
                        throw Failure(StatusCode.FailedPrecondition, "Unexpected node message.");

                    if (request.BodyCase == ConnectRequest.BodyOneofCase.SeatEvent &&
                        application is INodeEventApplication)
                    {
                        using var eventCallbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                            callbackCancellation!.Token);
                        var eventCallbackTask = NodeLinkRegistry.ReceiveAsync(activeSession, request,
                            eventCallbackCancellation.Token);
                        var acknowledgement = await waitForEventCallback(eventCallbackTask, activeSession,
                            eventCallbackCancellation).ConfigureAwait(false);
                        if (acknowledgement is not null)
                            await writeEventAcknowledgement(acknowledgement).ConfigureAwait(false);
                    }
                    else
                    {
                        var callbackTask = NodeLinkRegistry.ReceiveAsync(activeSession, request,
                            callbackCancellation!.Token);
                        await waitForCallback(callbackTask, activeSession).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (RpcException) { throw; }
        catch (Exception) when (context.CancellationToken.IsCancellationRequested)
        { throw Failure(StatusCode.Cancelled, string.Empty); }
        catch (Exception)
        { throw Failure(StatusCode.Internal, "Node link processing failed."); }
        finally
        {
            livenessSource?.Cancel();
            livenessSource?.Dispose();
            if (connected && !disconnected && session is not null && !session.Superseded.IsCompleted)
            {
                await stateChanged(NodeLinkState.Disconnected).ConfigureAwait(false);
                await stateTail.ConfigureAwait(false);
            }
            callbackCancellation?.Dispose();
            if (session is not null)
                registry.Remove(identity, session);
        }
    }

    private async Task<ConnectRequest> ReadHelloAsync(IAsyncStreamReader<ConnectRequest> requestStream,
        ServerCallContext context, TimeSpan timeout)
    {
        using var deadlineSource = new CancellationTokenSource(timeout, timeProvider);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, deadlineSource.Token);
        bool hasFirstMessage;
        try { hasFirstMessage = await requestStream.MoveNext(timeoutSource.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        { throw Failure(StatusCode.FailedPrecondition, "Hello must be the first node message."); }
        catch (RpcException exception) when (timeoutSource.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested && exception.StatusCode == StatusCode.Cancelled)
        { throw Failure(StatusCode.FailedPrecondition, "Hello must be the first node message."); }
        catch (Exception) when (context.CancellationToken.IsCancellationRequested)
        { throw Failure(StatusCode.Cancelled, string.Empty); }
        catch (Exception)
        { throw Failure(StatusCode.Unavailable, "Node link unavailable."); }
        if (!hasFirstMessage || requestStream.Current.BodyCase != ConnectRequest.BodyOneofCase.Hello)
            throw Failure(StatusCode.FailedPrecondition, "Hello must be the first node message.");
        return requestStream.Current;
    }

    private static RpcException Failure(StatusCode code, string detail) => new(new Status(code, detail));
}
