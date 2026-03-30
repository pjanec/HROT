using System;
using System.Collections.Concurrent;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;

namespace Bagira.Common.Orchestration
{
    /// <summary>
    /// CycloneDDS implementation of <see cref="IOrchestrationTransport"/>.
    ///
    /// <para>Bridges between the generic toolkit types (<see cref="OrchestrationCommand"/>,
    /// <see cref="OrchestrationStatus"/>) and the Bagira DDS message types
    /// (<see cref="NodeOpCommand"/>, <see cref="NodeOpStatus"/>, <see cref="NodeHeartbeat"/>).
    /// </para>
    ///
    /// <para>Command ingestion runs on a dedicated background <see cref="Thread"/> that
    /// enqueues <see cref="OrchestrationCommand"/> values; dequeuing happens on the main
    /// thread inside <see cref="FDP.Toolkit.Orchestration.DrillSlave.Tick"/>.</para>
    /// </summary>
    public sealed class DdsOrchestrationTransport : IOrchestrationTransport
    {
        private readonly DdsWriter<NodeHeartbeat>  _heartbeatWriter;
        private readonly DdsReader<NodeOpCommand>  _commandReader;
        private readonly DdsWriter<NodeOpStatus>   _statusWriter;
        private readonly ConcurrentQueue<OrchestrationCommand> _inboundQueue = new();
        private readonly Thread _listenerThread;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        /// <summary>
        /// Raw DDS status writer — exposed so legacy Bagira handlers that still accept
        /// <c>DdsWriter&lt;NodeOpStatus&gt;</c> can be wired without change during the
        /// G0402→G0404 migration window.  Removed once all handlers use the transport.
        /// </summary>
        public DdsWriter<NodeOpStatus> StatusWriter => _statusWriter;

        /// <param name="participant">DDS domain participant owned by the calling subsystem.</param>
        /// <param name="nodeId">
        /// Node roster ID used to filter inbound <see cref="NodeOpCommand"/> messages —
        /// only commands addressed to this node are enqueued.
        /// </param>
        public DdsOrchestrationTransport(DdsParticipant participant, int nodeId)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            _heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
            _commandReader   = new DdsReader<NodeOpCommand>(participant);
            _statusWriter    = new DdsWriter<NodeOpStatus>(participant);

            // Filter to commands addressed to this node.
            _commandReader.SetFilter(cmd => cmd.TargetNodeId == nodeId);

            _listenerThread = new Thread(() => RunListener(_cts.Token))
            {
                IsBackground = true,
                Name = $"Node{nodeId}-DrillSlave-Transport",
            };
            _listenerThread.Start();
        }

        /// <inheritdoc />
        public void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc)
        {
            _heartbeatWriter.Write(new NodeHeartbeat
            {
                NodeId          = nodeId,
                SubsystemName   = subsystemName,
                LocalDsmState   = (DSMState)localStateId,
                WallTicksUtc    = wallTicksUtc,
                CpuUsagePercent = 0f,
                RamUsedBytes    = 0L,
                SimTickAdvancing = false,
                SubsystemsJson  = string.Empty,
            });
        }

        /// <inheritdoc />
        public void PublishStatus(OrchestrationStatus status)
        {
            _statusWriter.Write(new NodeOpStatus
            {
                TransactionId   = status.TransactionId,
                NodeId          = status.NodeId,
                StatusCode      = status.StatusCode,
                IsParticipating = status.IsParticipating,
                ResultJson      = status.ResultJson ?? string.Empty,
            });
        }

        /// <inheritdoc />
        public bool TryDequeueCommand(out OrchestrationCommand cmd) =>
            _inboundQueue.TryDequeue(out cmd);

        private void RunListener(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                using var scope = _commandReader.Take();
                foreach (var sample in scope)
                {
                    if (!sample.IsValid) continue;
                    var raw = sample.Data;
                    _inboundQueue.Enqueue(new OrchestrationCommand(
                        raw.TransactionId,
                        raw.TargetNodeId,
                        (int)raw.Operation,
                        raw.PayloadJson ?? string.Empty));
                }
                Thread.Sleep(1);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _listenerThread.Join(TimeSpan.FromSeconds(2));
            _cts.Dispose();
            _commandReader.Dispose();
            _statusWriter.Dispose();
            _heartbeatWriter.Dispose();
        }
    }
}
