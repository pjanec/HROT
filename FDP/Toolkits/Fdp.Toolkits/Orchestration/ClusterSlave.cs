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
        private readonly NodeRole _roles;   // P1: the node's declared role mask (now the SOURCE of fdp.role.* tokens).
        private readonly FdpEventBus? _eventBus;

        // CE-285 (C-cap): the node's static capability token set — fdp.role.* tokens derived from _roles
        // (AQ-70 §Q70-C: the mask is no longer published; its tokens are) UNION the feature tokens the
        // composition root passed (e.g. CapabilityTokens.ReliableInit). Published ONCE at join on the durable
        // NodeCapabilities descriptor, never on the per-tick heartbeat (§Q70-B).
        private string[] _capabilityTokens;
        private bool _capabilitiesPublished;

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
            FdpEventBus? eventBus = null,
            NodeRole roles = NodeRole.None,
            IReadOnlyList<string>? capabilities = null)
        {
            _nodeId        = nodeId;
            _subsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
            _eventBus      = eventBus;
            _roles         = roles;
            _capabilityTokens = BuildCapabilityTokens(roles, capabilities);
            EnsureOrchestrationEventsRegistered(eventBus);
        }

        // ── Test-only constructor ─────────────────────────────────────────────

        /// <summary>
        /// Creates a ClusterSlave for tests.
        /// Use <see cref="EnqueueIntentForTest"/> to inject intents directly.
        /// </summary>
        public ClusterSlave(FdpEventBus? eventBus = null, int nodeId = 0, string subsystemName = "TestNode",
            NodeRole roles = NodeRole.None, IReadOnlyList<string>? capabilities = null)
        {
            _nodeId        = nodeId;
            _subsystemName = subsystemName;
            _eventBus      = eventBus;
            _roles         = roles;
            _capabilityTokens = BuildCapabilityTokens(roles, capabilities);
            EnsureOrchestrationEventsRegistered(eventBus);
        }

        /// <summary>The capability tokens this node advertises (or has advertised) at join.</summary>
        public IReadOnlyList<string> CapabilityTokens => _capabilityTokens;

        /// <summary>
        /// ⭐ Adds tokens a host can only compute AFTER the slave exists — e.g. the asset tokens, which need the host's
        /// asset catalog (docs/DESIGN_Asset_Management.md §10 D4). ⛔ Only before the one publish at join: the capability
        /// descriptor is durable and published ONCE, so a later append would be silently invisible to the cluster —
        /// it throws instead.
        /// </summary>
        public void AppendCapabilities(IEnumerable<string> tokens)
        {
            if (_capabilitiesPublished)
                throw new InvalidOperationException(
                    $"[ClusterSlave] node {_nodeId}: capabilities were already published at join; appending now would never reach the cluster.");
            _capabilityTokens = BuildCapabilityTokens(NodeRole.None, _capabilityTokens.Concat(tokens).ToList());
        }

        /// <summary>The full capability token set this node advertises: the <c>fdp.role.*</c> tokens for its
        /// role mask (AQ-70 §Q70-C — the mask is derived back from these at ingest) unioned with the feature
        /// tokens the composition root supplied. Order-stable and de-duplicated.</summary>
        private static string[] BuildCapabilityTokens(NodeRole roles, IReadOnlyList<string>? featureTokens)
        {
            var set = new List<string>();
            foreach (var t in NodeRoleTokens.TokensFromMask(roles))
                if (!set.Contains(t)) set.Add(t);
            if (featureTokens != null)
                foreach (var t in featureTokens)
                    if (!string.IsNullOrEmpty(t) && !set.Contains(t)) set.Add(t);
            return set.ToArray();
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
            // CE-285 (C-cap): advertise the static capability token set ONCE at join. Durable descriptor →
            // one publish is retained + delivered to the orchestrator and any late joiner (§Q70-B).
            if (!_capabilitiesPublished && _eventBus != null)
            {
                _capabilitiesPublished = true;
                _eventBus.PublishManaged(new NodeCapabilitiesEvent
                {
                    NodeId       = _nodeId,
                    Capabilities = _capabilityTokens,
                });
            }

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
                    SessionId     = _followingSessionId,   // ⭐ C2
                    // CE-286 (C-roles): the heartbeat is telemetry-only; the role mask travels as fdp.role.*
                    // tokens on the durable NodeCapabilities descriptor (published once at join above).
                });
            }

            // ⭐⭐ C2 (Q91 §4-I I5, R-4006) — follow ONE cluster session.
            FollowClusterSession();
            FollowInventory();

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

                    // ⭐⭐ P8 (Q91 §4-H.3a, H8) — an abort naming the PENDING prepare's transaction does not wait in the
                    //   queue it exists to clear: it preempts, then runs now. ⛔ Otherwise a stuck load could never be
                    //   cancelled — the case R-4001 hands to the operator.
                    if (_pendingPrepare.HasValue
                     && intent.Operation == NodeOpType.AbortTransaction
                     && intent.DomainPayload is AbortTransactionPayload preempting
                     && preempting.TargetTransactionId == _pendingPrepare.Value.Intent.TransactionId)
                    {
                        PreemptPendingPrepare(preempting.TargetTransactionId);
                        DispatchIntent(intent);
                    }
                    else if (_pendingPrepare.HasValue)
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
                    // ⭐ The STACK, not just the message: a faulted prepare fails the whole 2PC and leaves an
                    //   empty world, and "Entity … missing NetworkIdentity" alone did not say which of a
                    //   dozen translators threw (2026-10-01). This path is an error and is rare; it can afford it.
                    FdpLog<ClusterSlave>.Error(
                        "[ClusterSlave] PrepareAsync faulted for operation {0} " +
                        "(transactionId={1}): {2}. Commit skipped.",
                        pending.Intent.Operation,
                        pending.Intent.TransactionId,
                        pending.PrepareTask.Exception?.GetBaseException().ToString() ?? "unknown");
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
                if (previousStateId != nextStateId) NotifyIfEnteredIdle();
                return;
            }

            // ⭐⭐ P8 (Q91 §4-H.3a, H7) — an abort goes to EVERY handler that claims it, never the first only.
            if (intent.Operation == NodeOpType.AbortTransaction)
            {
                DispatchAbort(intent);
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
                            prepareTask.Exception?.GetBaseException().ToString() ?? "unknown");
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
                    // ⭐⭐ P4 (Q91 §4-B2, R-4001) — say so ONCE: the prepare runs on, the frame (and the heartbeat) keeps
                    //   ticking, and the master reads this as progress, never as the answer.
                    _eventBus?.PublishManaged(new NodeOpCompletedEvent
                    {
                        TransactionId   = intent.TransactionId,
                        Operation       = intent.Operation,
                        NodeId          = _nodeId,
                        StatusCode      = OrchestrationStatusCode.InProgress,
                        IsParticipating = true,
                        ResultPayload   = null,
                    });
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

        /// <summary>
        /// ⭐⭐ P8 (Q91 §4-H.3a, H8) — drop the pending prepare of <paramref name="transactionId"/> and every queued intent
        /// of that transaction. ⚠ The prepare's task runs on to its end; its result is discarded, never committed — it
        /// built only private objects (§4-B2, H6). Its op is answered <c>Cancelled</c>.
        /// </summary>
        private void PreemptPendingPrepare(Guid transactionId)
        {
            var dropped = _pendingPrepare!.Value.Intent;
            _pendingPrepare = null;

            int before = _pendingIntents.Count;
            var keep = new Queue<ExecuteNodeOpIntent>(_pendingIntents.Count);
            foreach (var queued in _pendingIntents)
                if (queued.TransactionId != transactionId) keep.Enqueue(queued);
            _pendingIntents.Clear();
            foreach (var queued in keep) _pendingIntents.Enqueue(queued);

            FdpLog<ClusterSlave>.Warn(
                "[ClusterSlave] Abort preempts the pending {0} of transaction {1}; {2} queued intent(s) of it dropped.",
                dropped.Operation, transactionId, before - _pendingIntents.Count);
            _eventBus?.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = dropped.TransactionId,
                Operation       = dropped.Operation,
                NodeId          = _nodeId,
                StatusCode      = OrchestrationStatusCode.Cancelled,
                IsParticipating = true,
                ResultPayload   = null,
            });
        }

        /// <summary>
        /// ⭐⭐ P8 (Q91 §4-H.3a, H7) — an abort names a TRANSACTION, so every handler holding part of it rolls its part
        /// back; a handler holding nothing for it no-ops. ⛔ First-match dispatch let whichever handler registered first
        /// swallow every abort. One answer is sent for the abort op.
        /// </summary>
        private void DispatchAbort(ExecuteNodeOpIntent intent)
        {
            bool participating = false;
            bool failed        = false;
            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(intent)) continue;
                participating = true;

                var task = handler.PrepareAsync(intent, default);
                if (!task.IsCompleted)
                {
                    // ⛔ An abort must not wait: a handler whose abort is asynchronous is a defect, reported loudly.
                    FdpLog<ClusterSlave>.Error(
                        "[ClusterSlave] {0} answered AbortTransaction asynchronously — its rollback was not committed.",
                        handler.GetType().Name);
                    failed = true;
                    continue;
                }
                if (task.IsFaulted)
                {
                    FdpLog<ClusterSlave>.Error("[ClusterSlave] {0} failed to abort: {1}",
                        handler.GetType().Name, task.Exception?.GetBaseException().ToString() ?? "unknown");
                    failed = true;
                    continue;
                }
                handler.Commit(intent, repo: null);
            }

            _eventBus?.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = intent.TransactionId,
                Operation       = intent.Operation,
                NodeId          = _nodeId,
                StatusCode      = failed ? OrchestrationStatusCode.Failure : OrchestrationStatusCode.Success,
                IsParticipating = participating,
                ResultPayload   = null,
            });
        }

        // ── C2 — the cluster session this node follows ────────────────────────

        /// <summary>
        /// ⭐⭐ C2 (Q91 §4-I I5, R-4006) — the cluster session this node follows (empty until it has seen one). Rides the heartbeat.
        /// </summary>
        public Guid FollowingSessionId => _followingSessionId;
        private Guid _followingSessionId;

        /// <summary>The cluster state last announced by the master this node follows (<c>Idle</c> until one is seen).</summary>
        private ClusterState _clusterState = ClusterState.Idle;

        /// <summary>
        /// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6) — the cluster state the master LAST ANNOUNCED to this node (a public read of what it already
        /// follows). ⚠ Not <see cref="LocalClusterState"/>: that is this node's own COMMITTED state, which stays <c>Idle</c> while the
        /// cluster is already loading — the start gate needs the CLUSTER's. Meaningful only when <see cref="HasFollowedState"/>.
        /// </summary>
        public ClusterState FollowedClusterState => _clusterState;

        /// <summary>⭐ <c>CE-4096</c> — a real (non-MasterGone) broadcast has reached this node, so <see cref="FollowedClusterState"/> is a
        /// fact and not the <c>Idle</c> default of "no master seen yet".</summary>
        public bool HasFollowedState { get; private set; }

        /// <summary>⭐ <c>CE-4096</c> — the master's last word was <c>MasterGone</c>: set by that broadcast, cleared by the next real one.</summary>
        public bool MasterGone { get; private set; }

        /// <summary>
        /// ⭐⭐ <c>R-4007</c> (Q91 §4-M) — <b>is this node BEHIND its cluster?</b> It follows a master's session, it is in
        /// <c>Idle</c>, and the cluster it follows is not: it has no world to put replicated state into, so its world ingress
        /// stays closed (<c>CycloneNetworkIngressSystem.IsJoinFrozen</c>) until it commits a load. ⭐ A cluster at rest in
        /// <c>Idle</c>, and a node with no master, are never frozen.
        /// </summary>
        public bool IsJoinFrozen =>
            _followingSessionId != Guid.Empty
            && _localStateId == (int)ClusterState.Idle
            && _clusterState != ClusterState.Idle;

        /// <summary>
        /// ⭐⭐ C2 — a DIFFERENT session id, or the master's cluster-state writer gone, means the world this node belonged to has
        /// ended: drop to <c>Idle</c> and forget whatever that world's master had asked for. ⭐ Deterministic, no timer: the node
        /// learns it is stale the moment the new master's durable sample arrives. ⚠ What the node does with the WORLD it still
        /// holds is not decided here (Q91 §4-I I6 ties it to <c>CE-4000</c>); the next load's world boundary clears it.
        /// </summary>
        /// <summary>⭐ R-3055 — the exercises the master can replay, as its last inventory said (DDS <c>AssetInventoryTopic</c>, through
        /// the node's slave translator); <see langword="null"/> until one arrives. The same read model on every node.</summary>
        public System.Collections.Generic.IReadOnlyList<ExerciseInventoryItem>? KnownExercises { get; private set; }

        /// <summary>⭐ CE-3329 R-3058 — the replay session's time axis, as the master's last cluster-state broadcast said (Sim until one
        /// arrives — the default). The same read model on every node.</summary>
        public ReplayAxis ReplayAxis { get; private set; } = ReplayAxis.Sim;

        /// <summary>⭐ CE-4090 (AQ108 D14) — the run the cluster shows was started with recording OFF, as the master's last cluster-state broadcast
        /// said (false until one arrives, and for a sender that does not set it). The same read model on every node.</summary>
        public bool RunNotRecorded { get; private set; }

        /// <summary>⭐ CE-4090 — the cluster's <i>Record live runs</i> setting is OFF, as the master's last broadcast said (false = ON, also before one arrives).</summary>
        public bool RecordLiveRunsOff { get; private set; }

        private void FollowInventory()
        {
            if (_eventBus == null) return;
            foreach (var ev in _eventBus.ReadManaged<AssetInventoryUpdateEvent>())
                KnownExercises = ev.LocalExercises ?? System.Array.Empty<ExerciseInventoryItem>();
        }

        private void FollowClusterSession()
        {
            if (_eventBus == null) return;
            foreach (var ev in _eventBus.ReadManaged<ClusterStateUpdateEvent>())
            {
                NotifyClusterStateUpdate(ev.CurrentState, ev.MasterGone);   // ⭐ CE-3329 W2 — the master's commit reached this node
                if (ev.MasterGone)
                {
                    if (_followingSessionId != Guid.Empty)
                        DropToIdle($"the master of session {_followingSessionId} is gone");
                    _followingSessionId = Guid.Empty;
                    _clusterState = ClusterState.Idle;
                    MasterGone = true;          // ⭐ CE-4096
                    continue;
                }
                MasterGone = false;             // ⭐ CE-4096 — the next real broadcast
                HasFollowedState = true;
                _clusterState = ev.CurrentState;
                ReplayAxis    = ev.ReplayAxis;   // ⭐ CE-3329 R-3058
                RunNotRecorded = ev.RunNotRecorded;   // ⭐ CE-4090 D14
                RecordLiveRunsOff = ev.RecordLiveRunsOff;
                if (ev.SessionId == Guid.Empty) continue;   // a sender that does not carry the session says nothing about it
                if (_followingSessionId != Guid.Empty && ev.SessionId != _followingSessionId)
                    DropToIdle($"a new cluster session {ev.SessionId} replaced {_followingSessionId}");
                _followingSessionId = ev.SessionId;
            }
        }

        /// <summary>⭐ CE-3329 W2 — every handler waiting for the master's commit (an egress hold) hears each broadcast.</summary>
        private void NotifyClusterStateUpdate(ClusterState state, bool masterGone)
        {
            foreach (var handler in _handlers)
            {
                if (handler is not IClusterStateUpdateObserver observer) continue;
                try { observer.OnClusterStateUpdate(state, masterGone); }
                catch (Exception ex)
                {
                    FdpLog<ClusterSlave>.Error("[ClusterSlave] Node {0}: {1}.OnClusterStateUpdate failed: {2}",
                        _nodeId, handler.GetType().Name, ex.Message);
                }
            }
        }

        private void DropToIdle(string why)
        {
            if (_pendingPrepare.HasValue)
            {
                var stale = _pendingPrepare.Value;
                _pendingPrepare = null;
                stale.Handler.Abort(stale.Intent, repo: null);
            }
            _pendingIntents.Clear();

            int previous = _localStateId;
            _localStateId = (int)ClusterState.Idle;
            if (previous != _localStateId)
            {
                _eventBus?.Publish(new TkClusterStateChangedEvent { PreviousStateId = previous, NextStateId = _localStateId });
                NotifyIfEnteredIdle();
            }
            FdpLog<ClusterSlave>.Warn("[ClusterSlave] Node {0} drops to Idle: {1}.", _nodeId, why);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4000</c> F2 / <c>R-4019</c> — the node has just ENTERED <c>Idle</c> (either route): every handler that owns
        /// the world empties it, so the node deletes what it owned on the wire and <c>Idle</c> is empty everywhere.
        /// </summary>
        private void NotifyIfEnteredIdle()
        {
            if (_localStateId != (int)ClusterState.Idle) return;
            foreach (var handler in _handlers)
            {
                if (handler is not IIdleResetHandler idle) continue;
                try { idle.OnEnteredIdle(); }
                catch (Exception ex)
                {
                    FdpLog<ClusterSlave>.Error("[ClusterSlave] Node {0}: {1}.OnEnteredIdle failed: {2}",
                        _nodeId, handler.GetType().Name, ex.Message);
                }
            }
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
