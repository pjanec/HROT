namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// Marker interface for handlers that participate in the Drill State Machine (DSM)
    /// two-phase-commit lifecycle.
    /// <para>
    /// Concrete handlers (e.g. <see cref="EcsRecordReplayController"/>) are registered
    /// with <see cref="DrillSlave.RegisterHandler"/> so that the slave can dispatch
    /// <c>NodeOpCommand</c> payloads to the appropriate handler and fan-out
    /// async <c>Prepare</c> calls via <c>Task.WhenAll</c>.
    /// </para>
    /// <para>
    /// Full 2PC dispatch (PrepareAsync / Commit / Abort) will be added in a future batch
    /// once <c>NodeOpCommand</c> and <c>NodeOpType</c> are defined.
    /// </para>
    /// </summary>
    public interface IDsmHandler
    {
        // Phase: 8 skeleton — full 2PC protocol to be added with DrillMaster/DrillSlave
        // implementation in a future batch.
    }
}
