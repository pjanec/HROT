using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

namespace Bagira.CGF.Modules.Orchestration
{
    /// <summary>
    /// CGF subsystem's drill state machine slave.
    ///
    /// <para>Publishes <see cref="NodeHeartbeat"/> at 1 Hz (wall-clock)
    /// and dispatches <see cref="NodeOpCommand"/> messages received from the
    /// <see cref="Bagira.Orchestrator.DrillMaster"/> to registered
    /// <see cref="IDsmHandler"/> instances.</para>
    ///
    /// <para>DDS ingestion runs on a dedicated background thread that only
    /// enqueues commands to a <see cref="ConcurrentQueue{T}"/>; dispatching
    /// happens on the main thread inside <see cref="Tick"/>.</para>
    ///
    /// <para><b>Prepare/Commit ordering (BATCH-19 A.2):</b>
    /// When a handler's <see cref="IDsmHandler.PrepareAsync"/> returns an incomplete
    /// <see cref="Task"/>, <see cref="Tick"/> defers <see cref="IDsmHandler.Commit"/>
    /// until the task completes (stored in <c>_pendingPrepare</c>).  This prevents
    /// <c>Commit</c> from racing async prepare work — the same pattern applied to
    /// <see cref="Bagira.SimHost.Modules.Orchestration.DrillSlave"/> in BATCH-18.</para>
    /// </summary>
    public sealed class DrillSlave : IDisposable
    {
        private readonly DdsWriter<NodeHeartbeat>? _heartbeatWriter;
        private readonly DdsReader<NodeOpCommand>? _commandReader;
        private readonly DdsWriter<NodeOpStatus>?  _nodeOpStatusWriter;
        private readonly ConcurrentQueue<NodeOpCommand> _pendingCommands = new();
        private readonly List<IDsmHandler> _handlers = new();
        private readonly Stopwatch _heartbeatTimer = Stopwatch.StartNew();
        private readonly int _nodeId;
        private readonly string _subsystemName;

        // ── Pending async prepare (BATCH-19 A.2) ─────────────────────────────
        /// <summary>
        /// Holds a <c>PrepareAsync</c> task that has not yet completed, together with the
        /// originating command and handler.  When set, <see cref="Tick"/> defers processing
        /// new commands and calls <see cref="IDsmHandler.Commit"/> only once the task is done,
        /// ensuring correct <c>PrepareAsync → Commit</c> ordering for handlers whose prepare
        /// work is genuinely async.
        /// </summary>
        private (Task<string?> PrepareTask, NodeOpCommand Cmd, IDsmHandler Handler)? _pendingPrepare;

        private Thread? _listenerThread;
        private CancellationTokenSource? _listenerCts;
        private bool _disposed;

        // ── Test-only constructor ─────────────────────────────────────────────

        /// <summary>
        /// Creates a DrillSlave without DDS writers/readers.
        /// Heartbeat publishing and command ingestion are disabled.
        /// Used by unit/integration tests that exercise handler dispatch without DDS.
        /// </summary>
        internal DrillSlave() { _nodeId = 0; _subsystemName = "CGF-Test"; }

        // ── Production constructor ────────────────────────────────────────────

        /// <param name="participant">DDS domain participant owned by the calling application.</param>
        /// <param name="nodeId">Unique integer node identifier for this subsystem instance.</param>
        /// <param name="subsystemName">Human-readable name published in <see cref="NodeHeartbeat.SubsystemName"/>.</param>
        public DrillSlave(DdsParticipant participant, int nodeId, string subsystemName)
        {
            _nodeId = nodeId;
            _subsystemName = subsystemName;
            _heartbeatWriter    = new DdsWriter<NodeHeartbeat>(participant);
            _commandReader      = new DdsReader<NodeOpCommand>(participant);
            _nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);
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

        /// <summary>
        /// DDS writer for <see cref="NodeOpStatus"/> ACKs.
        /// Exposed so handlers (e.g. <see cref="Handlers.StoryLoadDsmHandler"/>) can publish
        /// acknowledgements back to the orchestrator.  <c>null</c> in DDS-less test paths.
        /// </summary>
        internal DdsWriter<NodeOpStatus>? NodeOpStatusWriter => _nodeOpStatusWriter;

        /// <summary>Registers a DSM handler.  A handler may be registered only once.</summary>
        public void RegisterHandler(IDsmHandler handler)
        {
            if (!_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        /// <summary>
        /// Enqueues a command directly into the pending queue without DDS.
        /// For unit/integration testing only — bypasses the background listener thread.
        /// </summary>
        internal void EnqueueCommandForTest(NodeOpCommand cmd) =>
            _pendingCommands.Enqueue(cmd);

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

            // BATCH-19 A.2: drain any pending async PrepareAsync before accepting new commands.
            // Guarantees Commit is never called before PrepareAsync finishes.
            if (_pendingPrepare.HasValue)
            {
                var pending = _pendingPrepare.Value;
                if (!pending.PrepareTask.IsCompleted)
                    return; // still in flight; process next tick

                _pendingPrepare = null;
                if (pending.PrepareTask.IsFaulted)
                {
                    FdpLog<DrillSlave>.Error(
                        "[CGF.DrillSlave] PrepareAsync faulted for operation {0} " +
                        "(transactionId={1}): {2}. Commit skipped.",
                        pending.Cmd.Operation,
                        pending.Cmd.TransactionId,
                        pending.PrepareTask.Exception?.GetBaseException().Message);
                    return;
                }

                pending.Handler.Commit(pending.Cmd, repo: null);
            }

            while (_pendingCommands.TryDequeue(out var cmd))
            {
                DispatchCommand(cmd);
                // If DispatchCommand stored a new pending task, stop dequeuing until next tick.
                if (_pendingPrepare.HasValue) break;
            }
        }

        private void PublishHeartbeat()
        {
            _heartbeatWriter!.Write(new NodeHeartbeat
            {
                NodeId = _nodeId,
                SubsystemName = _subsystemName,
                LocalDsmState = DSMState.Standby,
                WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
                CpuUsagePercent = 0f,
                RamUsedBytes = 0L,
                SimTickAdvancing = false,
                SubsystemsJson = string.Empty,
            });
        }

        private void DispatchCommand(NodeOpCommand cmd)
        {
            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(cmd.Operation)) continue;

                // BATCH-19 A.2: defer Commit when PrepareAsync is genuinely async.
                var prepareTask = handler.PrepareAsync(cmd, default);
                if (prepareTask.IsCompleted)
                {
                    if (prepareTask.IsFaulted)
                        FdpLog<DrillSlave>.Error(
                            "[CGF.DrillSlave] PrepareAsync faulted for operation {0} " +
                            "(transactionId={1}): {2}. Commit skipped.",
                            cmd.Operation, cmd.TransactionId,
                            prepareTask.Exception?.GetBaseException().Message);
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
                "[CGF.DrillSlave] No handler for NodeOpCommand {0}.", cmd.Operation);
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
            _nodeOpStatusWriter?.Dispose();
            _heartbeatWriter?.Dispose();
        }
    }
}
