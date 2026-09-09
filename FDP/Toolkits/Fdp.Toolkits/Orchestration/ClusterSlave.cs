using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Fdp.Toolkit.Orchestration
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
        private readonly System.Collections.Generic.HashSet<(Guid, NodeOpType, ClusterState)> _seenTransactionIds = new();

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
            EnsureOrchestrationEventsRegistered(eventBus);
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
            EnsureOrchestrationEventsRegistered(eventBus);
        }

        /// <summary>
        /// ⭐ Registers the orchestration event vocabulary on the bus this slave was handed, so a
        /// <see cref="ClusterSlave"/> can always publish what it publishes.
        ///
        /// <para>
        /// 🔴 <b>Why it lives here and not in each subsystem's bootstrap.</b> Under <c>--mode all</c>
        /// every subsystem gets its OWN isolated <see cref="FdpEventBus"/>, so
        /// <c>OrchestrationEventRegistry.RegisterAll</c> called during one subsystem's bootstrap does
        /// nothing for another's. It was called by <c>CgfApplication</c>, <c>EditorSubsystem</c> and
        /// <c>HrotNodeBuilder</c> — and NOT by IG. The first <c>Tick()</c> therefore published
        /// <c>NodeHeartbeatEvent</c> on an unregistered stream and strict mode killed the whole
        /// process on frame one:
        /// <c>"Strict Mode Violation: Managed event type 'NodeHeartbeatEvent' was published without
        /// being explicitly registered."</c>
        /// </para>
        ///
        /// <para>
        /// ⭐ Registering from the constructor makes the guarantee follow the publisher instead of
        /// relying on every present and future host to remember. The existing bootstrap calls stay
        /// correct and harmless — registration is <c>GetOrCreate</c>, so it is idempotent.
        /// </para>
        ///
        /// <para>⚠ A null bus means "no I/O" (test/offline shape) and needs nothing registered.</para>
        /// </summary>
        private static void EnsureOrchestrationEventsRegistered(FdpEventBus? eventBus)
        {
            if (eventBus is null) return;
            OrchestrationEventRegistry.RegisterAll(eventBus);
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

            // Read new intents from bus FIRST.  When async prepare is running, unseen intents
            // are queued internally so they survive the next SwapBuffers().
            if (_eventBus != null)
            {
                foreach (var intent in _eventBus.ReadManaged<ExecuteNodeOpIntent>())
                {
                    // Drop intents targeted at other nodes (0 = broadcast).
                    if (intent.TargetNodeId != 0 && intent.TargetNodeId != _nodeId)
                        continue;

                    if (_pendingPrepare.HasValue)
                    {
                        // Async prepare in progress — buffer unseen intents for next tick.
                        ClusterState sd = intent.DomainPayload switch
                        {
                            CommitStatePayload      csp2 => csp2.TargetState,
                            EditLoadHandlerPayload  elp  => elp.TargetState,
                            _                            => (ClusterState)(-1),
                        };
                        if (!_seenTransactionIds.Contains((intent.TransactionId, intent.Operation, sd)))
                            _pendingIntents.Enqueue(intent);
                    }
                    else
                    {
                        DispatchIntent(intent);
                    }
                }
            }

            // Drain pending async prepare.
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
                        Operation       = pending.Intent.Operation,
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
                    Operation       = pending.Intent.Operation,
                    NodeId          = _nodeId,
                    StatusCode      = OrchestrationStatusCode.Success,
                    IsParticipating = true,
                    ResultPayload   = pendingResult,
                });
            }

            // Drain deferred intents queued while async prepare was active.
            while (_pendingIntents.Count > 0)
            {
                DispatchIntent(_pendingIntents.Dequeue());
                if (_pendingPrepare.HasValue)
                    break;
            }
        }

        // ── This node's committed cluster state ───────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-163</c> — THIS NODE'S COMMITTED cluster state.</b> The value set by the
        /// <c>CommitState</c> arm of <see cref="DispatchIntent"/>, republished as
        /// <c>TkClusterStateChangedEvent</c> and written into every heartbeat.
        ///
        /// <para>📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §1c.</para>
        ///
        /// <para>🔴 <b>Why it became public.</b> 📐 Measured on a four-process cluster
        /// <c>2026-09-03</c>: <c>POST /scenario/load/live {waitForReady:true}</c> answered
        /// <c>NOT_SUPPORTED_HERE(cluster.state)</c> on <b>every</b> node, while
        /// <c>{waitForReady:false}</c> published fine and the fan-out landed. Only the readiness READ was
        /// missing — and it was missing because <c>DebugApiService.CurrentClusterState()</c> has two arms
        /// and <b>both are <c>--mode all</c> arms</b> *(the editor's own getter, or a sibling subsystem's
        /// pumped <c>ClusterUiCache</c>)*. ⛔ In a separate-process cluster neither exists, so a node that
        /// KNEW its state refused to say so. ⇒ ⭐ the 12th instance of <c>CLAUDE.md</c>'s
        /// <i>"a production caller that HAS a dependency must PASS it"</i>, one layer down.</para>
        ///
        /// <para>⚠⚠ <b>READ THE SEMANTIC BEFORE USING IT.</b> This is <b>this node's committed state</b>,
        /// ⛔ <b>NOT the cluster's</b>. During a transition a node can legitimately lag the master, and two
        /// nodes can legitimately disagree. ⭐ That is the RIGHT answer for a readiness poll — the caller
        /// is asking <i>"is THIS node at the target?"</i> and a node reporting its own committed state has
        /// actually done the work — but ⛔ it must be reported as a node-local fact, never passed off as a
        /// cluster-wide one. 📄 <c>ClusterUiCache.CurrentState</c> is the cluster-wide view; the two are
        /// different facts, not two implementations of one.</para>
        ///
        /// <para>⭐ Before <c>CommitState</c> ever runs this is <c>0</c>, which is
        /// <see cref="ClusterState"/>'s first member — the honest "nothing committed yet" answer, and the
        /// same value the heartbeat carries.</para>
        /// </summary>
        public ClusterState LocalClusterState => (ClusterState)_localStateId;

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-164</c> — does this slave publish on <paramref name="bus"/>?</b> The composition
        /// invariant every networked slave node must satisfy: its <see cref="ClusterSlave"/> and its
        /// <c>ISlaveOrchestrationTranslator</c> sit on the <b>same, single</b> orchestration bus — the one
        /// <c>HrotNodeBuilder</c> put on <c>HrotNodeContext.EventBus</c>.
        ///
        /// <para>📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1b.
        /// Asserted by <c>SharedApplicationBootstrapper</c> right after Phase 5.</para>
        ///
        /// <para>🔴 <b>Why a PREDICATE and not a <c>Bus</c> property.</b> The invariant is
        /// <i>"is it THIS bus"</i>, and that is all a caller needs. ⛔ Exposing the bus itself would hand
        /// every caller a publish handle to the control plane to satisfy one assertion — a much wider
        /// surface than the question asked.</para>
        ///
        /// <para>📐 <b>The defect it catches, measured <c>2026-09-03</c>:</b> IG built its
        /// <see cref="ClusterSlave"/> on a second <c>FdpEventBus</c> of its own while the shared,
        /// egress-capable translator sat on the context's — so every <c>TransitionStateIntent</c> IG
        /// published was read by nothing, silently. ⚠ Nothing structural forbade it: the base's
        /// <c>BuildOrchestration</c> is <c>abstract</c>, so the 7-phase order mandates THAT a node wires
        /// orchestration and shares none of the doing. ⇒ ⭐ this is the binding that was missing.</para>
        /// </summary>
        public bool PublishesOn(FdpEventBus? bus) => ReferenceEquals(_eventBus, bus);

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
        ///
        /// <para>⚠ Kept as the raw <c>int</c> the heartbeat carries, so the existing wire-level assertions
        /// stay wire-level. ⭐ Production readers want <see cref="LocalClusterState"/>.</para>
        /// </summary>
        public int LocalStateIdForTest => _localStateId;

        // ── Private helpers ───────────────────────────────────────────────────

        private void DispatchIntent(ExecuteNodeOpIntent intent)
        {
            // Idempotency: drop re-delivered intents.
            // CommitState and PrepareState (with EditLoadHandlerPayload) intents for different
            // target states within the same transaction must each be accepted — use the payload
            // target-state int as a discriminant so same-transaction PrepareState ops don't collide.
            ClusterState stateDiscriminant = intent.DomainPayload switch
            {
                CommitStatePayload     csp => csp.TargetState,
                EditLoadHandlerPayload elp => elp.TargetState,
                _                         => (ClusterState)(-1),
            };

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
                int nextStateId = intent.DomainPayload is CommitStatePayload cp ? (int)cp.TargetState : _localStateId;
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
                if (!handler.CanHandle(intent)) continue;

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
                            Operation       = intent.Operation,
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
                            Operation       = intent.Operation,
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
            _eventBus?.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = intent.TransactionId,
                Operation       = intent.Operation,
                NodeId          = _nodeId,
                StatusCode      = OrchestrationStatusCode.Success,
                IsParticipating = false,
                ResultPayload   = null,
            });
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
