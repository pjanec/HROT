using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Generic cluster state machine slave.
    ///
    /// <para>Publishes a node heartbeat at 1 Hz via <see cref="FdpEventBus"/>
    /// and dispatches inbound <see cref="ExecuteNodeOpIntent"/> messages to registered
    /// <see cref="IClusterStateHandler"/> instances.  All network I/O is routed through
    /// the event bus, keeping this class free of any Hrot or CycloneDDS references.</para>
    ///
    /// <para><b>Prepare/Commit ordering:</b> when a handler's
    /// <see cref="IClusterStateHandler.PrepareAsync"/> returns an incomplete <see cref="System.Threading.Tasks.Task"/>,
    /// the result is stored in <c>_pendingPrepare</c> and <see cref="IClusterStateHandler.Commit"/> is
    /// deferred to the next <see cref="Tick"/> that sees the task complete.</para>
    ///
    /// <para><b>Deduplication:</b> intents with a <see cref="ExecuteNodeOpIntent.TransactionId"/>
    /// already seen for the same operation are silently dropped.</para>
    /// </summary>
    public sealed class ClusterSlave : IDisposable
    {
        private readonly int    _nodeId;
        private readonly string _subsystemName;
        private readonly FdpEventBus? _eventBus;

        private readonly List<IClusterStateHandler> _handlers = new();
        private readonly Stopwatch _heartbeatTimer = Stopwatch.StartNew();

        // ── Deduplication (CGF1-S0202) ────────────────────────────────────────
        // 3-tuple key (TransactionId, Operation, stateDiscriminant) so that PrepareXxx
        // and CommitState belonging to the same 2PC transaction are each accepted once,
        // and multiple CommitState intents for different target states in the same
        // multi-step trajectory are NOT collapsed into one (DEBT-007 fix).
        private readonly System.Collections.Generic.HashSet<(Guid, NodeOpType, int)> _seenTransactionIds = new();

        // ── Deferred intents (DEBT-007 fix) ──────────────────────────────────
        // When an async prepare is running, any new intents from the bus are buffered
        // here so they survive the next SwapBuffers() call instead of being silently lost.
        private readonly System.Collections.Generic.Queue<ExecuteNodeOpIntent> _pendingIntents = new();

        // ── Pending async prepare (BATCH-18 pattern) ─────────────────────────
        private (System.Threading.Tasks.Task<object?> PrepareTask, ExecuteNodeOpIntent Intent, IClusterStateHandler Handler)? _pendingPrepare;

        // ── Local state id ────────────────────────────────────────────────────
        private int _localStateId;

        private bool _disposed;

        // ── Production constructor ────────────────────────────────────────────

        /// <summary>
        /// Creates a ClusterSlave backed by the <paramref name="eventBus"/> for all I/O.
        /// </summary>
        /// <param name="nodeId">Node identifier published in heartbeats.</param>
        /// <param name="subsystemName">Subsystem name published in heartbeats.</param>
        /// <param name="eventBus">Optional event bus for heartbeat and operation event publication.</param>
        public ClusterSlave(
            int    nodeId,
            string subsystemName,
            FdpEventBus? eventBus = null)
        {
            _nodeId        = nodeId;
            _subsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
            _eventBus      = eventBus;
        }

        // ── Test-only constructor ─────────────────────────────────────────────

        /// <summary>
        /// Creates a ClusterSlave for tests.
        /// Use <see cref="EnqueueIntentForTest"/> to inject intents directly.
        /// </summary>
        public ClusterSlave(FdpEventBus? eventBus = null, int nodeId = 0, string subsystemName = "TestNode")
        {
            _nodeId        = nodeId;
            _subsystemName = subsystemName;
            _eventBus      = eventBus;
        }

        // ── Handler registration ──────────────────────────────────────────────

        /// <summary>Registers a Cluster handler.  A handler may be registered only once.</summary>
        public void RegisterHandler(IClusterStateHandler handler)
        {
            if (!_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        /// <summary>
        /// Returns <c>true</c> when at least one handler of type <typeparamref name="T"/>
        /// is registered, either directly or wrapped in a
        /// <see cref="Hrot.Common.Orchestration.HrotHandlerAdapter"/>.
        /// </summary>
        public bool IsHandlerRegistered<T>() where T : IClusterStateHandler =>
            _handlers.OfType<T>().Any();

        /// <summary>All registered Cluster state handlers.</summary>
        public IReadOnlyList<IClusterStateHandler> RegisteredHandlers => _handlers;

        // ── Per-frame tick ────────────────────────────────────────────────────

        /// <summary>
        /// Publishes a heartbeat (if 1 s has elapsed), drains tickable handlers, resolves
        /// any pending prepare task, and dispatches new commands from the event bus.
        /// Call once per application frame from the main thread.
        /// </summary>
        public void Tick()
        {
            // Heartbeat at 1 Hz via FdpEventBus.
            if (_heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
            {
                _heartbeatTimer.Restart();
                _eventBus?.PublishManaged(new NodeHeartbeatEvent
                {
                    NodeId        = _nodeId,
                    LocalStateId  = _localStateId,
                    WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
                    SubsystemName = _subsystemName,
                });
            }

            // Poll tickable handlers for deferred ACKs.
            foreach (var handler in _handlers)
            {
                if (handler is ITickableClusterStateHandler tickable)
                    tickable.DrainDeferredAcks();
            }

            // Drain pending async prepare before accepting new commands.
            if (_pendingPrepare.HasValue)
            {
                var pending = _pendingPrepare.Value;
                if (!pending.PrepareTask.IsCompleted)
                    return;

                _pendingPrepare = null;
                if (pending.PrepareTask.IsFaulted)
                {
                    FdpLog<ClusterSlave>.Error(
                        "[ClusterSlave] PrepareAsync faulted for operation {0} " +
                        "(transactionId={1}): {2}. Commit skipped.",
                        pending.Intent.Operation,
                        pending.Intent.TransactionId,
                        pending.PrepareTask.Exception?.GetBaseException().Message ?? "unknown");
                    _eventBus?.PublishManaged(new NodeOpCompletedEvent
                    {
                        TransactionId   = pending.Intent.TransactionId,
                        NodeId          = _nodeId,
                        StatusCode      = OrchestrationStatusCode.Failure,
                        IsParticipating = true,
                        ResultPayload   = null,
                    });
                    _pendingIntents.Clear();  // Discard deferred intents for the failed transaction
                    return;
                }

                var pendingResult = pending.PrepareTask.Result;
                pending.Handler.Commit(pending.Intent, repo: null);
                _eventBus?.PublishManaged(new NodeOpCompletedEvent
                {
                    TransactionId   = pending.Intent.TransactionId,
                    NodeId          = _nodeId,
                    StatusCode      = OrchestrationStatusCode.Success,
                    IsParticipating = true,
                    ResultPayload   = pendingResult,
                });
            }

            // Drain deferred intents queued in a previous tick (when async prepare was active).
            while (_pendingIntents.Count > 0 && !_pendingPrepare.HasValue)
            {
                DispatchIntent(_pendingIntents.Dequeue());
            }

            // Read new intents from bus.  When async prepare is running, unseen intents
            // are queued internally so they survive the next SwapBuffers().
            if (_eventBus != null)
            {
                foreach (var intent in _eventBus.ConsumeManaged<ExecuteNodeOpIntent>())
                {
                    if (_pendingPrepare.HasValue)
                    {
                        // Async prepare in progress — buffer unseen intents for next tick.
                        int sd = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is int v ? v : -1;
                        if (!_seenTransactionIds.Contains((intent.TransactionId, intent.Operation, sd)))
                            _pendingIntents.Enqueue(intent);
                    }
                    else
                    {
                        DispatchIntent(intent);
                    }
                }
            }
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Enqueues an intent directly — bypasses transport.  For unit tests only.
        /// </summary>
        public void EnqueueIntentForTest(ExecuteNodeOpIntent intent)
        {
            DispatchIntent(intent);
        }

        /// <summary>
        /// Current local state id as it would be written into the next heartbeat.
        /// For unit-test assertions only.
        /// </summary>
        public int LocalStateIdForTest => _localStateId;

        // ── Private helpers ───────────────────────────────────────────────────

        private void DispatchIntent(ExecuteNodeOpIntent intent)
        {
            // Idempotency: drop re-delivered intents.
            // CommitState intents for different target states within the same transaction
            // must each be accepted — use DomainPayload (target state int) as a discriminant.
            // All other intents use discriminant -1.
            int stateDiscriminant = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is int sd
                ? sd : -1;
            var dedupKey = (intent.TransactionId, intent.Operation, stateDiscriminant);
            if (!_seenTransactionIds.Add(dedupKey))
            {
                FdpLog<ClusterSlave>.Debug(
                    "[ClusterSlave] Duplicate intent {0}-{1} dropped.", intent.TransactionId, intent.Operation);
                return;
            }

            // CommitState: update local state and raise TkClusterStateChangedEvent.
            if (intent.Operation == NodeOpType.CommitState)
            {
                int nextStateId = intent.DomainPayload is int stateId ? stateId : _localStateId;
                var previousStateId = _localStateId;
                _localStateId = nextStateId;
                _eventBus?.Publish(new TkClusterStateChangedEvent
                {
                    PreviousStateId = previousStateId,
                    NextStateId     = nextStateId,
                });
                return;
            }

            // Handler dispatch with async-prepare deferral.
            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(intent.Operation)) continue;

                var prepareTask = handler.PrepareAsync(intent, default);
                if (prepareTask.IsCompleted)
                {
                    if (prepareTask.IsFaulted)
                    {
                        FdpLog<ClusterSlave>.Error(
                            "[ClusterSlave] PrepareAsync faulted for operation {0} " +
                            "(transactionId={1}): {2}. Commit skipped.",
                            intent.Operation, intent.TransactionId,
                            prepareTask.Exception?.GetBaseException().Message ?? "unknown");
                        _eventBus?.PublishManaged(new NodeOpCompletedEvent
                        {
                            TransactionId   = intent.TransactionId,
                            NodeId          = _nodeId,
                            StatusCode      = OrchestrationStatusCode.Failure,
                            IsParticipating = true,
                            ResultPayload   = null,
                        });
                    }
                    else
                    {
                        var result = prepareTask.Result;
                        handler.Commit(intent, repo: null);
                        _eventBus?.PublishManaged(new NodeOpCompletedEvent
                        {
                            TransactionId   = intent.TransactionId,
                            NodeId          = _nodeId,
                            StatusCode      = OrchestrationStatusCode.Success,
                            IsParticipating = true,
                            ResultPayload   = result,
                        });
                    }
                }
                else
                {
                    _pendingPrepare = (prepareTask, intent, handler);
                }
                return;
            }

            FdpLog<ClusterSlave>.Debug(
                "[ClusterSlave] No handler for operation {0}.", intent.Operation);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
