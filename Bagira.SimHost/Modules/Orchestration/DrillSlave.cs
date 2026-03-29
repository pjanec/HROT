using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// SimHost drill state machine slave.
    ///
    /// <para>Publishes <see cref="NodeHeartbeat"/> at 1 Hz (wall-clock)
    /// and dispatches <see cref="NodeOpCommand"/> messages received from the
    /// <see cref="Bagira.Orchestrator.DrillMaster"/> to registered
    /// <see cref="IDsmHandler"/> instances.</para>
    ///
    /// <para>DDS ingestion runs on a dedicated background thread that only
    /// enqueues commands to a <see cref="ConcurrentQueue{T}"/>; dispatching
    /// happens on the main / ECS thread inside <see cref="Tick"/>.</para>
    /// </summary>
    public sealed class DrillSlave : IDisposable
    {
        private readonly DdsWriter<NodeHeartbeat>? _heartbeatWriter;
        private readonly DdsReader<NodeOpCommand>? _commandReader;
        private readonly ConcurrentQueue<NodeOpCommand> _pendingCommands = new();
        private readonly List<IDsmHandler> _handlers = new();
        private readonly Stopwatch _heartbeatTimer = Stopwatch.StartNew();
        private readonly int _nodeId;
        private readonly string _subsystemName;

        // ── Event bus (optional; injected for testability) ────────────────────
        private readonly FdpEventBus? _eventBus;

        // ── Current local DSM state (updated on each CommitState) ─────────────
        private DSMState _localDsmState = DSMState.Standby;

        // ── Duplicate-TransactionId deduplication (CGF1-S0202) ───────────────
        /// <summary>
        /// Set of already-processed <see cref="NodeOpCommand.TransactionId"/> values used
        /// to silently drop re-delivered DDS reliable messages.
        /// </summary>
        private readonly HashSet<Guid> _seenTransactionIds = new();

        private Thread? _listenerThread;
        private CancellationTokenSource? _listenerCts;
        private bool _disposed;

        // ── DDS-less constructor (internal: used only by NodeBootstrapper for non-orchestration roles) ──

        /// <summary>
        /// Creates a DrillSlave without DDS writers/readers.
        /// Heartbeat publishing and command ingestion are disabled.
        /// Not available for production orchestration roles; use <see cref="DrillSlave(DdsParticipant, int, string, FdpEventBus?)"/>.
        /// </summary>
        internal DrillSlave() { }

        /// <summary>
        /// Creates a DrillSlave without DDS writers/readers but with the provided event bus.
        /// Used by unit tests that exercise handler dispatch and event publication without DDS.
        /// </summary>
        internal DrillSlave(FdpEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        // ── Production constructor ────────────────────────────────────────────

        /// <param name="participant">DDS domain participant owned by the calling application.</param>
        /// <param name="nodeId">Unique integer node identifier for this subsystem instance.</param>
        /// <param name="subsystemName">Human-readable name published in <see cref="NodeHeartbeat.SubsystemName"/>.</param>
        /// <param name="eventBus">Optional event bus used to publish <see cref="DsmStateChangedEvent"/> on commit.</param>
        public DrillSlave(DdsParticipant participant, int nodeId, string subsystemName, FdpEventBus? eventBus = null)
        {
            _nodeId = nodeId;
            _subsystemName = subsystemName;
            _eventBus = eventBus;
            _heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
            _commandReader = new DdsReader<NodeOpCommand>(participant);
            // Only process commands addressed to this node's roster ID.
            _commandReader.SetFilter(cmd => cmd.TargetNodeId == _nodeId);

            _listenerCts = new CancellationTokenSource();
            _listenerThread = new Thread(() => RunCommandListener(_listenerCts.Token))
            {
                IsBackground = true,
                Name = $"{subsystemName}-DrillSlave-Listener"
            };
            _listenerThread.Start();
        }

        // ── Handler registration ──────────────────────────────────────────────

        /// <summary>Registers a DSM handler. A handler may be registered only once.</summary>
        public void RegisterHandler(IDsmHandler handler)
        {
            if (!_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        /// <summary>
        /// Returns <c>true</c> when at least one handler of type
        /// <typeparamref name="T"/> is registered.
        /// </summary>
        public bool IsHandlerRegistered<T>() where T : IDsmHandler =>
            _handlers.OfType<T>().Any();

        /// <summary>All registered DSM handlers.</summary>
        public IReadOnlyList<IDsmHandler> RegisteredHandlers => _handlers;

        /// <summary>
        /// Enqueues a command directly into the pending queue without DDS.
        /// For unit testing only — bypasses the background listener thread.
        /// </summary>
        internal void EnqueueCommandForTest(NodeOpCommand cmd) =>
            _pendingCommands.Enqueue(cmd);

        // ── Per-frame tick ────────────────────────────────────────────────────

        /// <summary>
        /// Publishes the next <see cref="NodeHeartbeat"/> (if 1 s has elapsed)
        /// and dispatches all pending <see cref="NodeOpCommand"/> messages.
        /// Call once per application frame from the main / ECS thread.
        /// </summary>
        public void Tick()
        {
            if (_heartbeatWriter != null && _heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
            {
                _heartbeatTimer.Restart();
                PublishHeartbeat();
            }

            while (_pendingCommands.TryDequeue(out var cmd))
                DispatchCommand(cmd);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void PublishHeartbeat()
        {
            _heartbeatWriter!.Write(new NodeHeartbeat
            {
                NodeId = _nodeId,
                SubsystemName = _subsystemName,
                LocalDsmState = _localDsmState,
                WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
                CpuUsagePercent = 0f,
                RamUsedBytes = 0L,
                SimTickAdvancing = false,
                SubsystemsJson = string.Empty,
            });
        }

        /// <summary>
        /// Current local DSM state as it would be written into the next heartbeat.
        /// Exposed for unit-test assertions only — do not use in production code.
        /// </summary>
        internal DSMState LocalDsmStateForTest => _localDsmState;

        private void DispatchCommand(NodeOpCommand cmd)
        {
            // ── Idempotency: silently drop re-delivered commands ──────────────
            if (!_seenTransactionIds.Add(cmd.TransactionId))
            {
                FdpLog<DrillSlave>.Debug(
                    "[SimHost.DrillSlave] Duplicate TransactionId {0} dropped.", cmd.TransactionId);
                return;
            }

            // ── CommitState: update local state and raise DsmStateChangedEvent ─
            if (cmd.Operation == NodeOpType.CommitState)
            {
                if (int.TryParse(cmd.PayloadJson, out var stateInt))
                {
                    var nextState = (DSMState)stateInt;
                    PublishDsmStateChanged(_localDsmState, nextState);
                    _localDsmState = nextState;
                }
                else
                {
                    FdpLog<DrillSlave>.Warn(
                        "[SimHost.DrillSlave] CommitState payload '{0}' could not be parsed as DSMState.",
                        cmd.PayloadJson);
                }
                return;
            }

            // ── Handler dispatch ──────────────────────────────────────────────
            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(cmd.Operation)) continue;
                _ = handler.PrepareAsync(cmd, default);
                handler.Commit(cmd, repo: null);
                return;
            }
            FdpLog<DrillSlave>.Debug(
                "[SimHost.DrillSlave] No handler for NodeOpCommand {0}.", cmd.Operation);
        }

        /// <summary>
        /// Publishes a <see cref="DsmStateChangedEvent"/> to the injected event bus.
        /// No-op when no bus was provided (non-orchestration roles).
        /// </summary>
        internal void PublishDsmStateChanged(DSMState previous, DSMState next)
        {
            _eventBus?.Publish(new DsmStateChangedEvent { Previous = previous, Next = next });
        }

        private void RunCommandListener(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                using var scope = _commandReader!.Take();
                foreach (var sample in scope)
                {
                    if (!sample.IsValid) continue;
                    _pendingCommands.Enqueue(sample.Data);
                }
                Thread.Sleep(1);
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listenerCts?.Cancel();
            _listenerThread?.Join(TimeSpan.FromSeconds(2));
            _listenerCts?.Dispose();
            _listenerCts = null;
            _listenerThread = null;
            _commandReader?.Dispose();
            _heartbeatWriter?.Dispose();
        }
    }
}

