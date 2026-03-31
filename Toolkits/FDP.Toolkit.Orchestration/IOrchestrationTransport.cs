namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Abstracts the underlying wire protocol (e.g. CycloneDDS) from the generic
    /// <c>ClusterSlave</c> dispatch engine.  Implementations bridge toolkit plain
    /// types to the actual transport layer.
    ///
    /// <para>
    /// The Hrot-layer implementation (<c>DdsOrchestrationTransport</c>) maps
    /// <see cref="OrchestrationCommand"/> ↔ <c>NodeOpCommand</c> and
    /// <see cref="OrchestrationStatus"/> ↔ <c>NodeOpStatus</c> DDS structs.
    /// </para>
    /// </summary>
    public interface IOrchestrationTransport : IDisposable
    {
        /// <summary>
        /// Publishes a liveness heartbeat for this node.
        /// Called approximately once per second by <c>ClusterSlave.Tick()</c>.
        /// </summary>
        void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc);

        /// <summary>
        /// Publishes an operation status ACK back to the orchestrator.
        /// Handlers call this via the transport reference injected at construction.
        /// </summary>
        void PublishStatus(OrchestrationStatus status);

        /// <summary>
        /// Attempts to dequeue one pending inbound command.
        /// Returns <c>false</c> when the queue is empty.
        /// Called by <c>ClusterSlave.Tick()</c> from the main thread.
        /// </summary>
        bool TryDequeueCommand(out OrchestrationCommand cmd);
    }
}
