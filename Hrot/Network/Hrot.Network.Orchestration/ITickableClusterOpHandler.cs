namespace Hrot.Common.Orchestration
{
    /// <summary>
    /// Optional extension for <see cref="IClusterOpHandler"/> implementations that produce
    /// deferred async acknowledgements (e.g. <c>CheckpointClusterOpHandler</c> whose disk I/O
    /// completes off the main thread).
    ///
    /// <para>
    /// <c>ClusterSlave.Tick()</c> calls <see cref="DrainDeferredAcks"/> on every registered
    /// handler that implements this interface, allowing handlers to publish
    /// <c>NodeOpStatus(Success/Failure)</c> ACKs as background work completes.
    /// </para>
    /// </summary>
    public interface ITickableClusterOpHandler : IClusterOpHandler
    {
        /// <summary>
        /// Called each frame from <c>ClusterSlave.Tick()</c>. Implementations check for
        /// completed deferred operations and publish any pending <c>NodeOpStatus</c> ACKs.
        /// </summary>
        void DrainDeferredAcks();
    }
}
