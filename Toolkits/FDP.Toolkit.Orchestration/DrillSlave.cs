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
    /// Generic drill state machine slave.
    ///
    /// <para>Publishes a node heartbeat at 1 Hz via <see cref="IOrchestrationTransport"/>
    /// and dispatches inbound <see cref="OrchestrationCommand"/> messages to registered
    /// <see cref="IDsmHandler"/> instances.  All DDS I/O is delegated to the transport,
    /// keeping this class free of any Bagira or CycloneDDS references.</para>
    ///
    /// <para><b>Prepare/Commit ordering:</b> when a handler's
    /// <see cref="IDsmHandler.PrepareAsync"/> returns an incomplete <see cref="System.Threading.Tasks.Task"/>,
    /// the result is stored in <c>_pendingPrepare</c> and <see cref="IDsmHandler.Commit"/> is
    /// deferred to the next <see cref="Tick"/> that sees the task complete.</para>
    ///
    /// <para><b>Deduplication:</b> commands with a <see cref="OrchestrationCommand.TransactionId"/>
    /// already seen are silently dropped.</para>
    /// </summary>
    public sealed class DrillSlave : IDisposable
    {
        private readonly IOrchestrationTransport? _transport;
        private readonly int    _nodeId;
        private readonly string _subsystemName;
        private readonly FdpEventBus? _eventBus;

        private readonly List<IDsmHandler> _handlers = new();
        private readonly Stopwatch _heartbeatTimer = Stopwatch.StartNew();

        // ── Deduplication (CGF1-S0202) ────────────────────────────────────────
        private readonly System.Collections.Generic.HashSet<Guid> _seenTransactionIds = new();

        // ── Pending async prepare (BATCH-18 pattern) ─────────────────────────
        private (System.Threading.Tasks.Task<string?> PrepareTask, OrchestrationCommand Cmd, IDsmHandler Handler)? _pendingPrepare;

        // ── Local state id ────────────────────────────────────────────────────
        private int _localStateId;

        private bool _disposed;

        // ── Production constructor ────────────────────────────────────────────

        /// <summary>
        /// Creates a DrillSlave backed by <paramref name="transport"/> for all DDS I/O.
        /// When <paramref name="transport"/> is <c>null</c>, heartbeat publishing and
        /// command polling are disabled (standalone / test mode without DDS).
        /// </summary>
        /// <param name="transport">DDS (or other) transport; owned by the caller.  May be <c>null</c>.</param>
        /// <param name="nodeId">Node identifier published in heartbeats.</param>
        /// <param name="subsystemName">Subsystem name published in heartbeats.</param>
        /// <param name="eventBus">Optional event bus for <see cref="TkDsmStateChangedEvent"/> publication.</param>
        public DrillSlave(
            IOrchestrationTransport? transport,
            int    nodeId,
            string subsystemName,
            FdpEventBus? eventBus = null)
        {
            _transport     = transport;
            _nodeId        = nodeId;
            _subsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
            _eventBus      = eventBus;
        }

        // ── Test-only constructor ─────────────────────────────────────────────

        /// <summary>
        /// Creates a DrillSlave without a transport.  Heartbeat publishing is disabled.
        /// Use <see cref="EnqueueCommandForTest"/> to inject commands without DDS.
        /// </summary>
        internal DrillSlave(FdpEventBus? eventBus = null)
        {
            _transport     = null;
            _nodeId        = 0;
            _subsystemName = string.Empty;
            _eventBus      = eventBus;
        }

        // ── Handler registration ──────────────────────────────────────────────

        /// <summary>Registers a DSM handler.  A handler may be registered only once.</summary>
        public void RegisterHandler(IDsmHandler handler)
        {
            if (!_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        /// <summary>
        /// Returns <c>true</c> when at least one handler of type <typeparamref name="T"/>
        /// is registered, either directly or wrapped in a
        /// <see cref="Bagira.Common.Orchestration.BagiraHandlerAdapter"/>.
        /// </summary>
        public bool IsHandlerRegistered<T>() where T : IDsmHandler =>
            _handlers.OfType<T>().Any();

        /// <summary>All registered DSM handlers.</summary>
        public IReadOnlyList<IDsmHandler> RegisteredHandlers => _handlers;

        // ── Per-frame tick ────────────────────────────────────────────────────

        /// <summary>
        /// Publishes a heartbeat (if 1 s has elapsed), drains tickable handlers, resolves
        /// any pending prepare task, and dispatches new commands from the transport.
        /// Call once per application frame from the main thread.
        /// </summary>
        public void Tick()
        {
            // Heartbeat at 1 Hz.
            if (_transport != null && _heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
            {
                _heartbeatTimer.Restart();
                _transport.PublishHeartbeat(_nodeId, _subsystemName, _localStateId,
                    DateTimeOffset.UtcNow.Ticks);
            }

            // Poll tickable handlers for deferred ACKs.
            foreach (var handler in _handlers)
            {
                if (handler is ITickableDsmHandler tickable)
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
                    FdpLog<DrillSlave>.Error(
                        "[DrillSlave] PrepareAsync faulted for operationId {0} " +
                        "(transactionId={1}): {2}. Commit skipped.",
                        pending.Cmd.OperationId,
                        pending.Cmd.TransactionId,
                        pending.PrepareTask.Exception?.GetBaseException().Message ?? "unknown");
                    return;
                }

                pending.Handler.Commit(pending.Cmd, repo: null);
            }

            if (_transport == null) return;

            while (_transport.TryDequeueCommand(out var cmd))
            {
                DispatchCommand(cmd);
                if (_pendingPrepare.HasValue) break;
            }
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Enqueues a command directly — bypasses transport.  For unit tests only.
        /// </summary>
        internal void EnqueueCommandForTest(OrchestrationCommand cmd)
        {
            DispatchCommand(cmd);
        }

        /// <summary>
        /// Current local state id as it would be written into the next heartbeat.
        /// For unit-test assertions only.
        /// </summary>
        internal int LocalStateIdForTest => _localStateId;

        // ── Private helpers ───────────────────────────────────────────────────

        // CommitState integer value = NodeOpType.CommitState = 2 (stable, must not change).
        private const int CommitStateOperationId = 2;

        private void DispatchCommand(OrchestrationCommand cmd)
        {
            // Idempotency: silently drop re-delivered commands.
            if (!_seenTransactionIds.Add(cmd.TransactionId))
            {
                FdpLog<DrillSlave>.Debug(
                    "[DrillSlave] Duplicate TransactionId {0} dropped.", cmd.TransactionId);
                return;
            }

            // CommitState: update local state and raise TkDsmStateChangedEvent.
            if (cmd.OperationId == CommitStateOperationId)
            {
                if (int.TryParse(cmd.PayloadJson, out var nextStateId))
                {
                    var previousStateId = _localStateId;
                    _localStateId = nextStateId;

                    _eventBus?.Publish(new TkDsmStateChangedEvent
                    {
                        PreviousStateId = previousStateId,
                        NextStateId     = nextStateId,
                    });
                }
                else
                {
                    FdpLog<DrillSlave>.Warn(
                        "[DrillSlave] CommitState payload '{0}' could not be parsed as int.",
                        cmd.PayloadJson);
                }
                return;
            }

            // Handler dispatch with async-prepare deferral.
            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(cmd.OperationId)) continue;

                var prepareTask = handler.PrepareAsync(cmd, default);
                if (prepareTask.IsCompleted)
                {
                    if (prepareTask.IsFaulted)
                        FdpLog<DrillSlave>.Error(
                            "[DrillSlave] PrepareAsync faulted for operationId {0} " +
                            "(transactionId={1}): {2}. Commit skipped.",
                            cmd.OperationId, cmd.TransactionId,
                            prepareTask.Exception?.GetBaseException().Message ?? "unknown");
                    else
                        handler.Commit(cmd, repo: null);
                }
                else
                {
                    _pendingPrepare = (prepareTask, cmd, handler);
                }
                return;
            }

            FdpLog<DrillSlave>.Debug(
                "[DrillSlave] No handler for operationId {0}.", cmd.OperationId);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport?.Dispose();
        }
    }
}
