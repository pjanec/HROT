using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

namespace Bagira.IOS.Orchestration
{
    /// <summary>
    /// IOS drill state machine slave — no-ECS variant.
    ///
    /// <para>Publishes <see cref="NodeHeartbeat"/> at 1 Hz (wall-clock)
    /// and dispatches <see cref="NodeOpCommand"/> messages from the
    /// <see cref="Bagira.Orchestrator.DrillMaster"/> to registered
    /// <see cref="IDsmHandler"/> instances.</para>
    ///
    /// <para>IOS has no <c>EntityRepository</c>; any handler that requires
    /// ECS state must guard against a <c>null</c> <c>repo</c> parameter.</para>
    ///
    /// <para>DDS ingestion runs on a dedicated background thread that only
    /// enqueues commands; dispatching happens on the calling thread inside
    /// <see cref="Tick"/>.</para>
    /// </summary>
    public sealed class DrillSlave : IDisposable
    {
        private readonly DdsWriter<NodeHeartbeat> _heartbeatWriter;
        private readonly DdsReader<NodeOpCommand> _commandReader;
        private readonly ConcurrentQueue<NodeOpCommand> _pendingCommands = new();
        private readonly List<IDsmHandler> _handlers = new();
        private readonly Stopwatch _heartbeatTimer = Stopwatch.StartNew();
        private readonly int _nodeId;
        private readonly string _subsystemName;

        private readonly Thread _listenerThread;
        private readonly CancellationTokenSource _listenerCts = new();
        private bool _disposed;

        /// <param name="participant">DDS domain participant owned by the calling application.</param>
        /// <param name="nodeId">Unique integer node identifier for this subsystem instance.</param>
        /// <param name="subsystemName">Human-readable name published in heartbeats.</param>
        public DrillSlave(DdsParticipant participant, int nodeId, string subsystemName)
        {
            _nodeId = nodeId;
            _subsystemName = subsystemName;
            _heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
            _commandReader = new DdsReader<NodeOpCommand>(participant);

            _listenerThread = new Thread(() => RunCommandListener(_listenerCts.Token))
            {
                IsBackground = true,
                Name = $"{subsystemName}-DrillSlave-Listener"
            };
            _listenerThread.Start();
        }

        /// <summary>Registers a DSM handler. A handler may be registered only once.</summary>
        public void RegisterHandler(IDsmHandler handler)
        {
            if (!_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        /// <summary>
        /// Publishes the next heartbeat (if 1 s has elapsed) and dispatches pending commands.
        /// Call once per application frame from the main IOS update loop.
        /// </summary>
        public void Tick()
        {
            if (_heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
            {
                _heartbeatTimer.Restart();
                PublishHeartbeat();
            }

            while (_pendingCommands.TryDequeue(out var cmd))
                DispatchCommand(cmd);
        }

        private void PublishHeartbeat()
        {
            _heartbeatWriter.Write(new NodeHeartbeat
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
                // IOS is no-ECS; pass repo: null unconditionally.
                _ = handler.PrepareAsync(cmd, default);
                handler.Commit(cmd, repo: null);
                return;
            }
            FdpLog<DrillSlave>.Debug("[IOS.DrillSlave] No handler for NodeOpCommand {0}.", cmd.Operation);
        }

        private void RunCommandListener(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                using var scope = _commandReader.Take();
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
            _listenerCts.Cancel();
            _listenerThread.Join(TimeSpan.FromSeconds(2));
            _listenerCts.Dispose();
            _commandReader.Dispose();
            _heartbeatWriter.Dispose();
        }
    }
}
