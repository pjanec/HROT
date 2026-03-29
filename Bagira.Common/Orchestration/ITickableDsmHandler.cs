namespace Bagira.Common.Orchestration
{
    /// <summary>
    /// Optional extension for <see cref="IDsmHandler"/> implementations that produce
    /// deferred async acknowledgements (e.g. <c>CheckpointDsmHandler</c> whose disk I/O
    /// completes off the main thread).
    ///
    /// <para>
    /// <c>DrillSlave.Tick()</c> calls <see cref="DrainDeferredAcks"/> on every registered
    /// handler that implements this interface, allowing handlers to publish
    /// <c>NodeOpStatus(Success/Failure)</c> ACKs as background work completes.
    /// </para>
    /// </summary>
    public interface ITickableDsmHandler : IDsmHandler
    {
        /// <summary>
        /// Called each frame from <c>DrillSlave.Tick()</c>. Implementations check for
        /// completed deferred operations and publish any pending <c>NodeOpStatus</c> ACKs.
        /// </summary>
        void DrainDeferredAcks();
    }
}
